using Calamelia.StockPrice.Serialize;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections;
using Calamelia.StockPrice.DataClient;

namespace Calamelia.StockPrice {
	public class PriceCollection<TKey> : IGrouping<TKey, Prices> {
		TKey _key;
		IEnumerable<SerializablePrices> _src;
		internal PriceCollection(TKey key,IEnumerable<SerializablePrices> src) {
			_key = key; _src = src;
		}
		internal PriceCollection(TKey key) : this(key, Enumerable.Empty<SerializablePrices>()) { }
		public TKey Key { get { return _key; } }
		internal IEnumerable<SerializablePrices> GetSource() { return _src; }
		public IEnumerator<Prices> GetEnumerator() {
			return _src.Select(a => new Prices(a)).GetEnumerator();
		}
		IEnumerator IEnumerable.GetEnumerator() {
			return this.GetEnumerator();
		}
	}
	public class TickerMarketPair {
		public TickerMarketPair(int ticker, string market) { this.TickerSymbol = ticker; Market = market; }
		public int TickerSymbol { get; private set; }
		public string Market { get; private set; }
		string _symbolName = "";
		public string SymbolName {
			get {
				if(string.IsNullOrEmpty(_symbolName)) {
					var p = new PriceCollection().LatestStocks().SingleOrDefault(a => a.TickerSymbol == TickerSymbol);
					if (p != null) _symbolName = p.SymbolName;
					else _symbolName = "unknown";
				}
				return _symbolName;
			}
		}
		private Tuple<int, string> getAsTuple() { return Tuple.Create(TickerSymbol, Market); }
		public override bool Equals(object obj) {
			var o = obj as TickerMarketPair;
			if (o == null) return false;
			return o.getAsTuple() == this.getAsTuple();
		}
		public override int GetHashCode() {
			return getAsTuple().GetHashCode();
		}
	}
	public class PriceCollection {
		/// <summary>データの取得元を指定しなかった場合適用されるデータソース。</summary>
		public static DataSource DefaultDataSource { get; set; } = DataSource.Kdb;

		public PriceCollection() : this(DefaultDataSource) { }
		public PriceCollection(DataSource source) { DataSource = source; }
		public DataSource DataSource { get; set; }

		public IEnumerable<DateTime> GetTimeLineScale() {
			IEnumerable<DateTime> src;
			switch (DataSource) {
			case DataSource.Kdb:
				src = KdbClient.GetTimeLineScale();
				break;
			default:
				throw new NotSupportedException();
			}
			return src;
		}

		#region stocks
		public PriceCollection<TickerMarketPair> MainStockLine(DateTime since, DateTime until, string symbolName) {
			var line = MainStockLine(since, until, symbolName, symbolName).SingleOrDefault();
			return line != null ? line : new PriceCollection<TickerMarketPair>(new TickerMarketPair(0000, "NotFound"));
		}
		public IEnumerable<PriceCollection<TickerMarketPair>> MainStockLine(DateTime since, DateTime until, string symbolName, params string[] symbolNames) {
			var sybs = new string[] { symbolName }.Union(symbolNames);
			var ids = LatestStocks()
				.Where(a => sybs.Contains(a.SymbolName))
				.Select(a => a.TickerSymbol)
				.ToArray();
			return mainStockLine(since, until, ids);
		}
		public PriceCollection<TickerMarketPair> MainStockLine(DateTime since, DateTime until, int ticker) {
			var line = MainStockLine(since, until, ()=>ticker,(a,b)=>a.TickerSymbol == b).SingleOrDefault();
			//var line = GetStocksSource(since, until, a => a.TickerSymbol).SingleOrDefault();
			return line != null ? line : new PriceCollection<TickerMarketPair>(new TickerMarketPair(ticker, "NotFound"));
		}
		public IEnumerable<PriceCollection<TickerMarketPair>> MainStockLine(DateTime since, DateTime until, int ticker, params int[] tickers) {
			var tiks = new int[] { ticker }.Union(tickers);
			return mainStockLine(since, until, tiks.ToArray());
		}
		IEnumerable<PriceCollection<TickerMarketPair>> mainStockLine(DateTime since, DateTime until, int[] tickers) {
			return MainStockLine(since, until, a => tickers.Contains(a.TickerSymbol));
		}
		public IEnumerable<PriceCollection<TickerMarketPair>> MainStockLine(DateTime since, DateTime until, Func<Prices, bool> pred) {
			return MainStockLine<object>(since, until, () => null, (a, b) => pred(a));
		}
		IEnumerable<PriceCollection<TickerMarketPair>> MainStockLine<T>(DateTime since, DateTime until, Func<T> key, Func<Prices, T, bool> pred) {
			IEnumerable<SerializablePrices> src;
			src = GetStocksSource(since, until, key, pred);
			foreach (var g in src.GroupBy(a => a.TickerSymbol)) {
				yield return margeToSingle(g);
			}
		}
		/// <summary>アクティブな市場を軸とした一つのラインを返す。</summary>
		PriceCollection<TickerMarketPair> margeToSingle(IEnumerable<SerializablePrices> src) {
			var ave = src.GroupBy(a => a.Market)
				.Select(a => new { Ave = a.Average(b => b.Turnover), Mkt = a.Key })
				.OrderBy(a => a.Ave)
				.Select((i, a) => new { Mkt = i.Mkt, Idx = a });
			Func<SerializablePrices, int> getNum = p => {
				var nm = ave.FirstOrDefault(a => a.Mkt == p.Market);
				return (nm != null) ? nm.Idx : -1;
			};
			Func<IEnumerable<SerializablePrices>, SerializablePrices> getMrg = a => {
				var idc = a.Select(b => new { Src = b, Num = getNum(b) })
					.OrderByDescending(b => b.Num)
					.First();
				return idc.Src;
			};
			var ssr = src.GroupBy(a => a.Date).Select(a => getMrg(a)).OrderBy(a => a.Date);
			string MktName = ssr.Last().Market;
			if (string.IsNullOrEmpty(MktName)) MktName = "unknown";
			return new PriceCollection<TickerMarketPair>(
				new TickerMarketPair(ssr.First().TickerSymbol, MktName),
				ssr);
		}

		public IGrouping<DateTime, Prices> TimeLineOfStocks(DateTime date) {
			var line = TimeLineOfStocks(date, date);
			return line.Any() ? line.First() : new Group<DateTime, Prices>(date);
		}
		public IEnumerable<IGrouping<DateTime, Prices>> TimeLineOfStocks(DateTime since, DateTime until, params int[] tickers) {
			if (tickers == null || !tickers.Any())
				return TimeLineOfStocks(since, until, a => true);
			else
				return TimeLineOfStocks(since, until, a => tickers.Contains(a.TickerSymbol));
		}
		public IEnumerable<IGrouping<DateTime, Prices>> TimeLineOfStocks(DateTime since, DateTime until, Func<Prices, bool> select) {
			return GetStocksSource(since, until,select)
				.Select(a => new Prices(a))
				.GroupBy(a => a.Date);
		}

		public IGrouping<DateTime, Prices> LatestStocks() {
			var dt = GetTimeLineScale().Max();
			var l = GetStocksSource(dt, dt, a => true);
			return new Group<DateTime, Prices>(dt, l.Select(a => new Prices(a)));
		}
		IEnumerable<SerializablePrices> GetStocksSource(DateTime since, DateTime until, Func<Prices, bool> pred) {
			return GetStocksSource<object>(since, until, () => null, (a, b) => pred(a));
		}
		IEnumerable<SerializablePrices> GetStocksSource<T>(DateTime since, DateTime until, Func<T> key, Func<Prices,T,bool> pred) {
			IEnumerable<SerializablePrices> src;
			switch (DataSource) {
			case DataSource.Kdb:
				src = KdbClient.Acquire(KdbData.stocks, since, until, a => pred(new Prices(a), key()));
				src = adjust(src);
				break;
			case DataSource.Yahoo:
				int ticker = (int)(object)key();
				src = YClient.Acquire(since, until, ticker);
				break;
			default:
				throw new NotSupportedException();
			}
			return src;
		}
		
		/// <summary>分割、無取引日の補正</summary>
		IEnumerable<SerializablePrices> adjust(IEnumerable<SerializablePrices> src) {
			foreach (var a in src.GroupBy(b => Tuple.Create(b.TickerSymbol, b.Market))) {
				var spl = new SplitInfos().StockLine(a.Key.Item1);
				double cur = 0;
				foreach (var b in a) {
					if (b.Turnover != 0) cur = b.ClosingPrice;
					else if (cur != 0 && b.Turnover == 0 && b.OpeningPrice == 0 && b.High == 0 && b.Low == 0 && b.ClosingPrice == 0) {
						b.OpeningPrice = cur;
						b.High = cur;
						b.Low = cur;
						b.ClosingPrice = cur;
					}
					b.AdjustedRate = spl.SplitRate(b.Date);
				}
			}
			return src;
		}
		#endregion stocks

		#region indices
		public PriceCollection<string> IndexLine(DateTime since, DateTime until, string indexName) {
			var line = IndexLine(since, until, a => a.SymbolName == indexName).SingleOrDefault();
			return line != null ? line : new PriceCollection<string>(indexName);
		}
		public IEnumerable<PriceCollection<string>> IndexLine(DateTime since, DateTime until, string indexName, params string[] indexNames) {
			var idxs = new string[] { indexName }.Union(indexNames);
			return IndexLine(since, until, a => idxs.Contains(a.SymbolName));
		}
		public IEnumerable<PriceCollection<string>> IndexLine(DateTime since, DateTime until, Func<Prices, bool> pred) {
			var d = GetIndicesSource(since, until, pred)
				.GroupBy(a => a.SymbolName);
			foreach (var dd in d) yield return new PriceCollection<string>(dd.Key, dd);
		}

		public IGrouping<DateTime, Prices> TimeLineOfIndices(DateTime date) {
			var line = TimeLineOfIndices(date, date);
			return line.Any() ? line.First() : new Group<DateTime, Prices>(date);
		}
		public IEnumerable<IGrouping<DateTime, Prices>> TimeLineOfIndices(DateTime since, DateTime until, params string[] indexNames) {
			if (indexNames == null || !indexNames.Any())
				return TimeLineOfIndices(since, until, a => true);
			else
				return TimeLineOfIndices(since, until, a => indexNames.Contains(a.SymbolName));
		}
		public IEnumerable<IGrouping<DateTime, Prices>> TimeLineOfIndices(DateTime since, DateTime until, Func<Prices, bool> pred) {
			return GetIndicesSource(since, until, pred)
				.Select(a => new Prices(a))
				.GroupBy(a => a.Date);
		}
		public IGrouping<DateTime, Prices> LatestIndices() {
			var dt = GetTimeLineScale().Max();
			var l = GetIndicesSource(dt, dt, a => true);
			return new Group<DateTime, Prices>(dt, l.Select(a => new Prices(a)));
		}
		IEnumerable<SerializablePrices> GetIndicesSource(DateTime since, DateTime until, Func<Prices, bool> pred) {
			IEnumerable<SerializablePrices> src;
			switch (DataSource) {
			case DataSource.Kdb:
				src = KdbClient.Acquire(KdbData.indices, since, until, a => pred(new Prices(a)));
				break;
			default:
				throw new NotSupportedException();
			}
			return src;
		}
		#endregion indices
	}
	public static class PriceCollectionHelper { 
		#region 拡張メソッドとその補助メソッド
		public static IEnumerable<IGrouping<TKey, Prices>> ScaleTo<TKey>(this IEnumerable<PriceCollection<TKey>> self, TimeScale scale) {
			foreach (var e in self) {
				yield return e.ScaleTo(scale);
			}
		}
		public static IGrouping<TKey,Prices> ScaleTo<TKey>(this PriceCollection<TKey> self, TimeScale scale) {
			return new Group<TKey,Prices>(self.Key, _scaleMarge(self, scale));
		}
		static IEnumerable<Prices> _scaleMarge(IEnumerable<Prices> priceData,TimeScale scale) {
			switch (scale) {
			case TimeScale.Daily:
				break;
			case TimeScale.Weekly:
				priceData = _summarize(priceData.GroupBy(x => weeklyChunk(x.Date)));
				break;
			case TimeScale.Monthly:
				priceData = _summarize(priceData.GroupBy(x => monthlyChunk(x.Date)));
				break;
			case TimeScale.Quarter:
				priceData = _summarize(priceData.GroupBy(x => quarterChunk(x.Date)));
				break;
			case TimeScale.Yearly:
				priceData = _summarize(priceData.GroupBy(x => yearlyChunk(x.Date)));
				break;
			}
			return priceData;
		}
		static IEnumerable<Prices> _summarize(IEnumerable<IGrouping<DateTime,Prices>> pricesGroup) {
			var d = pricesGroup.Select(x => new SerializablePrices() {
				Date = x.Max(a => a.Date), //x.First().Date,
				TickerSymbol = x.Last().TickerSymbol,
				SymbolName = x.Last().SymbolName,
				Market = x.Last().Market,
				OpeningPrice = x.First().OpeningPrice,
				ClosingPrice = x.Last().ClosingPrice,
				High = x.Max(h => h.High),
				Low = x.Min(l => l.Low),
				Turnover = x.Sum(t => t.Turnover),
			});
			return d.Select(a => new Prices(a));
		}
		static DateTime weeklyChunk(DateTime dt) {
			var dow = dt.DayOfWeek;
			switch (dow) {
			case DayOfWeek.Monday:
				return dt.AddDays(4);
			case DayOfWeek.Tuesday:
				return dt.AddDays(3);
			case DayOfWeek.Wednesday:
				return dt.AddDays(2);
			case DayOfWeek.Thursday:
				return dt.AddDays(1);
			case DayOfWeek.Friday:
				return dt;
			case DayOfWeek.Saturday:
				return dt.AddDays(-1);
			case DayOfWeek.Sunday:
				return dt.AddDays(-2);
			default:
				return dt;
			}
		}
		static DateTime monthlyChunk(DateTime dt) { return new DateTime(dt.Year, dt.Month, DateTime.DaysInMonth(dt.Year, dt.Month)); }
		static DateTime quarterChunk(DateTime dt) {
			if(dt.Month <= 3) {
				return new DateTime(dt.Year, 3, DateTime.DaysInMonth(dt.Year, 3));
			}else if(dt.Month <= 6) {
				return new DateTime(dt.Year, 6, DateTime.DaysInMonth(dt.Year, 6));
			}else if(dt.Month <= 9) {
				return new DateTime(dt.Year, 9, DateTime.DaysInMonth(dt.Year, 9));
			} else {
				return new DateTime(dt.Year, 12, DateTime.DaysInMonth(dt.Year, 12));
			}
		}
		static DateTime yearlyChunk(DateTime dt) { return new DateTime(dt.Year, 12, DateTime.DaysInMonth(dt.Year, 12)); }
		#endregion
	}
	internal class Group<TKey, TSrc> : IGrouping<TKey,TSrc> {
		TKey _key;
		IEnumerable<TSrc> _src;
		internal Group(TKey key,IEnumerable<TSrc> src) {
			_key = key;
			_src = src;
		}
		internal Group(TKey key) : this(key, Enumerable.Empty<TSrc>()) { }
		public TKey Key { get { return _key; } }
		public IEnumerator<TSrc> GetEnumerator() {
			return _src.GetEnumerator();
		}
		IEnumerator IEnumerable.GetEnumerator() {
			return _src.GetEnumerator();
		}
	}
}
