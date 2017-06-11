using System;
using System.Collections.Generic;
using System.Linq;
using Calamelia.StockPrice.DataClient;
using Calamelia.StockPrice.Serialize;

namespace Calamelia.StockPrice {
	public class SplitInfoCollection<Tkey> : IGrouping<Tkey,StockSplitInfo> {
		Tkey _key;
		IEnumerable<StockSplitInfo> _src;
		internal SplitInfoCollection(Tkey key, IEnumerable<StockSplitInfo> src) { _key = key; _src = src; }
		internal SplitInfoCollection(Tkey key) : this(key, Enumerable.Empty<StockSplitInfo>()) { }
		internal SplitInfoCollection(IGrouping<Tkey,StockSplitInfo> src) : this(src.Key, src) { }
		public Tkey Key {
			get { return _key; }
		}
		public IEnumerator<StockSplitInfo> GetEnumerator() {
			return _src.GetEnumerator();
		}
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
			return _src.GetEnumerator();
		}
		/// <summary>指定した日付の一株に対する現在の分割比。指定した時点での一株が現在では何株に分割されているかを示す値を返す。</summary>
		/// <param name="date">日付</param>
		public double SplitRate(DateTime date) {
			date = new DateTime(date.Year, date.Month, date.Day);
			double cur = 1;
			var r = _src.OrderByDescending(a => a.AvailableForSaleDate)
				.TakeWhile(a => date < a.AvailableForSaleDate);
			foreach(var s in r) {
				cur *= s.SplitRate;
			}
			return cur;
		}

	}
	public class SplitInfos {
		/// <summary>データの取得元を指定しなかった場合に適用されるデータソース。</summary>
		public static DataSource DefaultDataSource { get; set; } = DataSource.KabuDotCom;

		public SplitInfos() : this(DefaultDataSource) { }
		public SplitInfos(DataSource src) { this.DataSource = src; }

		public DataSource DataSource { get; set; }

		public SplitInfoCollection<int> StockLine(int ticker) {
			var line = StockLine(a => a.TickerSymbol == ticker);
			if (line.Count() == 1) return line.Single();
			else return new SplitInfoCollection<int>(ticker);
		}
		public IEnumerable<SplitInfoCollection<int>> StockLine(int ticker,params int[] tickers) {
			var tks = new int[] { ticker }.Union(tickers);
			return StockLine(a => tks.Contains(a.TickerSymbol));
		}
		public IEnumerable<SplitInfoCollection<int>> StockLine(Func<StockSplitInfo,bool> pred) {
			var grp = GetStockSplitInfo(pred).GroupBy(a => a.TickerSymbol);
			foreach(var a in grp) 
				yield return new SplitInfoCollection<int>(a);
		}
		public IGrouping<DateTime,StockSplitInfo> LatestSplitInfo(Func<StockSplitInfo,DateTime> keySelector) {
			var data = GetStockSplitInfo(a=>true).GroupBy(keySelector).OrderByDescending(a => a.Key);
			if (data.Any()) return data.First();
			else return new Group<DateTime,StockSplitInfo>(new DateTime());
		}
		public IEnumerable<StockSplitInfo> AllStockLine() {
			return GetStockSplitInfo(a => true);
		}
		IEnumerable<StockSplitInfo> GetStockSplitInfo(Func<StockSplitInfo,bool> pred) {
			IEnumerable<StockSplitInfo> grp;
			switch (DataSource) {
			case DataSource.KabuDotCom:
				grp = KabuDotComClient.Acquire(pred).Where(pred);
				break;
			default:
				throw new NotSupportedException();
			}
			return grp;
		}
	}
}
