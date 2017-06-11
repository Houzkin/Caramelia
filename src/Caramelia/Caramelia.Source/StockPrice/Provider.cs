using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Calamelia.StockPrice.Serialize;
using Calamelia.StockPrice.DataClient;

namespace Calamelia.StockPrice {
	public interface IStockPriceProvider {
		PriceCollection<TickerMarketPair> StockLine(DateTime since, DateTime until, int ticker);
	}
	public interface IStockPriceProviderTable :IStockPriceProvider{
		IEnumerable<PriceCollection<TickerMarketPair>> StockLine(DateTime since, DateTime until, int ticker, params int[] tickers);
		IEnumerable<PriceCollection<TickerMarketPair>> StockLine(DateTime since, DateTime until, Func<Prices, bool> pred);
		IEnumerable<DateTime> GetStockTimeLineScale();
		IGrouping<DateTime, Prices> TimeLineOfStocks(DateTime date);
		IEnumerable<IGrouping<DateTime, Prices>> TimeLineOfStocks(DateTime since, DateTime until, params int[] tickers);
		IEnumerable<IGrouping<DateTime, Prices>> TimeLineOfStocks(DateTime since, DateTime until, Func<Prices, bool> select);
		IGrouping<DateTime, Prices> LatestStocks();
	}
	
	public interface IFXProvider {
		PriceCollection<FXPair> FXLine(DateTime since, DateTime until, FXPair pair);
	}
	
	public sealed class YahooProvider : IStockPriceProvider, IFXProvider {

		public PriceCollection<TickerMarketPair> StockLine(DateTime since,DateTime until,int ticker) {
			var c = YClient.Acquire(since, until, ticker);
			return new PriceCollection<TickerMarketPair>(
				new TickerMarketPair(ticker, ""), c);
		}
		public PriceCollection<FXPair> FXLine(DateTime since, DateTime until, FXPair pair) {
			var c = YClient.Acquire(since, until, pair);
			return new PriceCollection<FXPair>(pair, c);
		}

	}
	public abstract class DataTableProvider {
		protected IEnumerable<SerializablePrices> Adjust(IEnumerable<SerializablePrices> src) {
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
		/// <summary>アクティブな市場を軸とした一つのラインを返す。</summary>
		protected PriceCollection<TickerMarketPair> margeToSingle(IEnumerable<SerializablePrices> src) {  {
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
		}}
	}
	public class KdbProvider : DataTableProvider, IStockPriceProviderTable {
		#region sticks
		public IEnumerable<PriceCollection<TickerMarketPair>> StockLine(DateTime since, DateTime until, int ticker, params int[] tickers) {
			var tiks = new int[] { ticker }.Union(tickers);
			return StockLine(since, until, a => tickers.Contains(a.TickerSymbol));
		}

		public PriceCollection<TickerMarketPair> StockLine(DateTime since, DateTime until, int ticker) {
			var line = StockLine(since, until, a => a.TickerSymbol == ticker).SingleOrDefault();
			return line != null ? line : new PriceCollection<TickerMarketPair>(new TickerMarketPair(ticker, "NotFound"));
		}
		public IEnumerable<PriceCollection<TickerMarketPair>> StockLine(DateTime since, DateTime until, Func<Prices, bool> pred) {
			foreach (var g in GetStockSource(since, until, pred).GroupBy(a => a.TickerSymbol))
				yield return margeToSingle(g);
		}
		public IEnumerable<DateTime> GetStockTimeLineScale() {
			return KdbClient.GetTimeLineScale();
		}
		public IGrouping<DateTime, Prices> LatestStocks() {
			var dt = GetStockTimeLineScale().Max();
			var l = GetStockSource(dt, dt, a => true);
			return new Group<DateTime, Prices>(dt, l.Select(a => new Prices(a)));
		}
		public IGrouping<DateTime, Prices> TimeLineOfStocks(DateTime date) {
			var line = TimeLineOfStocks(date, date);
			return line.Any() ? line.First() : new Group<DateTime, Prices>(date);
		}
		public IEnumerable<IGrouping<DateTime, Prices>> TimeLineOfStocks(DateTime since, DateTime until, params int[] tickers) {
			if (tickers == null || !tickers.Any()) return TimeLineOfStocks(since, until, a => true);
			else return TimeLineOfStocks(since, until, a => tickers.Contains(a.TickerSymbol));
		}
		public IEnumerable<IGrouping<DateTime, Prices>> TimeLineOfStocks(DateTime since, DateTime until, Func<Prices, bool> select) {
			return GetStockSource(since, until, select).Select(a => new Prices(a)).GroupBy(a => a.Date);
		}
		IEnumerable<SerializablePrices> GetStockSource(DateTime since,DateTime until,Func<Prices,bool> select) {
			return this.Adjust(KdbClient.Acquire(KdbData.stocks, since, until, a => select(new Prices(a))));
		}
		#endregion

		#region indices
		public PriceCollection<string> IndexLine(DateTime since,DateTime until,string indexName) {
			var line = IndexLine(since, until, a => a.SymbolName == indexName).SingleOrDefault();
			return line != null ? line : new PriceCollection<string>(indexName);
		}
		public IEnumerable<PriceCollection<string>> IndexLine(DateTime since,DateTime until,string indexName,params string[] indexNames) {
			var idxs = new string[] { indexName }.Union(indexNames);
			return IndexLine(since, until, a => idxs.Contains(a.SymbolName));
		}
		public IEnumerable<PriceCollection<string>> IndexLine(DateTime since, DateTime until,Func<Prices,bool> pred) {
			foreach (var d in GetIndicesSource(since, until, pred).GroupBy(a => a.SymbolName))
				yield return new PriceCollection<string>(d.Key, d);
		}
		public IGrouping<DateTime, Prices> LatestIndices() {
			var dt = this.GetStockTimeLineScale().Max();
			var l = GetIndicesSource(dt, dt, a => true);
			return new Group<DateTime, Prices>(dt, l.Select(a => new Prices(a)));
		}
		IEnumerable<SerializablePrices> GetIndicesSource(DateTime since,DateTime until,Func<Prices,bool> pred) {
			return KdbClient.Acquire(KdbData.indices, since, until, a => pred(new Prices(a)));
		}
		#endregion
	}

}
