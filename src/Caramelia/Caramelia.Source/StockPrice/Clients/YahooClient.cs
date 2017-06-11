using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using Calamelia.StockPrice.Serialize;
using HtmlAgilityPack;
using System.Xml.Serialization;
using System.IO;
using Caramelia;

namespace Calamelia.StockPrice.DataClient {
	
	/// <summary>ディレクトリ情報として使用する。</summary>
	enum yahooData { stock,summary,}

	internal class YClient {
		internal static IEnumerable<SerializablePrices> Acquire(DateTime since,DateTime until,int ticekr) {
			return Acquire(since, until, (object)ticekr);
		}
		internal static IEnumerable<SerializablePrices> Acquire(DateTime since,DateTime until,FXPair pair) {
			return Acquire(since, until, (object)pair);
		}
		static IEnumerable<SerializablePrices> Acquire(DateTime since,DateTime until,object ticker) {
			var src = getLocalData(yahooData.stock, ticker);
			if (download(since, until, ticker, ref src))
				save(src, yahooData.stock, ticker);
			return src;
		}
		#region IO
		static string localPath = Local.CurrentPath + "Yahoo" + Path.DirectorySeparatorChar;
		static string getFilePath(yahooData type, object ticker) { return localPath + type.ToString() + Path.DirectorySeparatorChar + ticker.ToString() + @".xml"; }
		/// <summary>ローカルに保存されているデータを取得する。</summary>
		static IEnumerable<SerializablePrices> getLocalData(yahooData type, object ticker) {
			string fileName = getFilePath(type, ticker);
			if (!File.Exists(fileName)) return new SerializablePrices[0];
			var deseri = new XmlSerializer(typeof(SerializablePrices[]));
			using (FileStream fs = new FileStream(fileName, FileMode.Open)) {
				SerializablePrices[] src;
				src = deseri.Deserialize(fs) as SerializablePrices[];
				StockPriceManager.SetMessage(DataSource.Yahoo, "読み込み完了");
				return src;
			}
		}
		/// <summary>データを保存する。</summary>
		static void save(IEnumerable<SerializablePrices> src, yahooData type,object ticker) {
			string fileName = getFilePath(type, ticker);
			createDirectoryIfNotFound(type);
			var seri = new XmlSerializer(typeof(SerializablePrices[]));
			using (FileStream fs = new FileStream(fileName, FileMode.Create)) {
				seri.Serialize(fs, src.ToArray());
			}
			StockPriceManager.SetMessage(DataSource.Yahoo, "保存完了");
		}
		static void createDirectoryIfNotFound(yahooData type) {
			if (!Directory.Exists(localPath + type.ToString())) 
				Directory.CreateDirectory(localPath + type.ToString());
		}
		#endregion
		#region download
		/// <summary>更新または再取得が必要だった場合はダウンロードを実行する。</summary>
		/// <returns>更新または再取得した場合は true</returns>
		static bool download(DateTime since,DateTime until,object ticker,ref IEnumerable<SerializablePrices> src) {
			since = since.Date;
			until = until.Date;
			if (!src.Any() || isWebChanged(ticker, src)) {
				src = _download(since, until, ticker);
				StockPriceManager.SetMessage(DataSource.Yahoo, "新規ダウンロードしました。");
				return true;
			} 
			var srcMx = src.Max(a => a.Date);
			var srcMn = src.Min(a => a.Date);

			if (srcMn <= since && until <= srcMx) return false;

			IEnumerable<SerializablePrices> dldata = Enumerable.Empty<SerializablePrices>();
			if (since < srcMn) dldata = dldata.Union(_download(since, srcMn.AddDays(-1), ticker));
			if (srcMx < until) dldata = dldata.Union(_download(srcMx.AddDays(1), until, ticker));
			var ald = src.Union(dldata);
			if (ald.Except(src).Any()) {
				src = ald;
				StockPriceManager.SetMessage(DataSource.Yahoo, "追加ダウンロードしました。");
				return true;
			}
			return false;
			
		}
		/// <summary>分割、併合などの変更があったかどうかを示す。</summary>
		static bool isWebChanged(object ticker, IEnumerable<SerializablePrices> src) {
			var mxSrc = src.Aggregate((a, b) => a.Date > b.Date ? a : b);
			var dl = _download(mxSrc.Date, mxSrc.Date, ticker).Single(a => a.Date == mxSrc.Date);
			return dl.AdjustedRate != mxSrc.AdjustedRate;
		}
		/// <summary>期間を指定して株価情報を取得する</summary>
		static IEnumerable<SerializablePrices> _download(DateTime since,DateTime until,object ticker) {
			//string url = @"http://stocks.finance.yahoo.co.jp/stocks/history/?code=" + ticker.ToString() + spanFormat(since, until);
			string url = spanFormat(ticker, since, until);
			var data = Enumerable.Empty<SerializablePrices>();
			var result = Enumerable.Empty<SerializablePrices>();
			int page = 1;
			do {
				data = _download(url + page.ToString());
				//result = result.Union(data);
				page++;
			} while (isContinue(since, until, data,ref result)/*data.Max(a => a.Date) < until*/);
			StockPriceManager.SetMessage(DataSource.Yahoo, since.ToShortDateString() +" -> "+until.ToShortDateString() + "ダウンロード完了");
			if (ticker is int) {
				foreach (var d in result) {
					d.TickerSymbol = (int)ticker;
					yield return d;
				}
			} else if (ticker is FXPair) {
				foreach(var d in result) {
					d.SymbolName = ticker.ToString();
					yield return d;
				}
			}
		}
		static bool isContinue(DateTime since,DateTime until,IEnumerable<SerializablePrices> downLoadData,ref IEnumerable<SerializablePrices> result) {
			if (!downLoadData.Any()) return false;
			if (!downLoadData.Except(result).Any()) return false;
			result = result.Union(downLoadData);
			var rstMn = result.Min(a => a.Date);
			var rstMx = result.Max(a => a.Date);
			return since < rstMn || rstMx < until;
		}
		static string spanFormat(object id,DateTime since,DateTime until) {
			if (id is int) {
				return @"http://stocks.finance.yahoo.co.jp/stocks/history/?code=" + id.ToString() + spanFormat(since,until);
			} else if (id is FXPair) {
				return @"http://info.finance.yahoo.co.jp/history/?code=" + id.ToString() + fxSpanFormat(since, until);
			} else throw new InvalidOperationException();
		}
		/// <summary>期間を示すURL部分を作成</summary>
		static string spanFormat(DateTime since, DateTime until) {
			string ur = ".T&sy=" + since.Year.ToString() + "&sm=" + since.Month.ToString() + "&sd=" + since.Day.ToString()
				+ "&ey=" + until.Year.ToString() + "&em=" + until.Month.ToString() + "&ed=" + until.Day.ToString() + "&tm=d&p=";
			return ur;
		}
		static string fxSpanFormat(DateTime since,DateTime until) {
			return @"%3DX&sy=" + since.Year.ToString() + "&sm="+since.Month.ToString()+"&sd="+since.Day.ToString()+
				"&ey="+until.Year.ToString()+"&em="+until.Month.ToString()+"&ed="+until.Day.ToString()+"&tm=d&p=";
		}
		static readonly WebClient wc = new WebClient() { Encoding = Encoding.UTF8 };
		/// <summary>指定したURLにアクセスし、株価情報を取得する。</summary>
		static IEnumerable<SerializablePrices> _download(string url) {
			var srcTxt = wc.DownloadString(url);
			HtmlDocument doc = new HtmlDocument();
			doc.LoadHtml(srcTxt);
			var d = doc.DocumentNode
				.SelectNodes(@"//div[@class=""padT12 marB10 clearFix""]")
				.Descendants("tr")
				.Select(a => new { Tds = a.Elements("td").Select(e => e.InnerText) })
				.Where(a => a.Tds.Any())
				.Select(a => parse(a.Tds));
			return d;
		}
		/// <summary>各要素から株価情報を生成</summary>
		static SerializablePrices parse(IEnumerable<string> items) {
			try {
				var sp = new SerializablePrices();
				sp.Date = DateTime.Parse(items.ElementAt(0));
				sp.OpeningPrice = double.Parse(items.ElementAt(1));
				sp.High = double.Parse(items.ElementAt(2));
				sp.Low = double.Parse(items.ElementAt(3));
				sp.ClosingPrice = double.Parse(items.ElementAt(4));
				if (7 <= items.Count()) {
					sp.Turnover = double.Parse(items.ElementAt(5));
					sp.AdjustedRate = sp.ClosingPrice / double.Parse(items.ElementAt(6));
				}
				return sp;
			} catch {
				throw;
			}
		}
		#endregion
	}

	//internal class YahooClient {
	//	/// <summary>指定した期間を最低限含むデータを取得する。</summary>
	//	/// <param name="symbol">銘柄コード</param>
	//	/// <param name="since">期間開始日</param>
	//	/// <param name="until">期間最終日</param>
	//	/// <param name="buff">現在取得済みのデータ</param>
	//	internal static IEnumerable<SerializableStockPrices> Acquire(int symbol,DateTime since,DateTime until, IEnumerable<SerializableStockPrices> buff) {
	//		var self = new YahooClient(symbol, since, until, buff);
	//		return self.Acquire();
	//	}
	//	DateTime _since;
	//	DateTime _until;
	//	IEnumerable<SerializableStockPrices> _buff;
	//	private YahooClient(int symbol, DateTime since, DateTime until, IEnumerable<SerializableStockPrices> buff) {
	//		codeUrl = "http://stocks.finance.yahoo.co.jp/stocks/history/?code=" + symbol.ToString();
	//		_buff = buff;
	//		_since = since;
	//		_until = until;
	//	}
	//	private IEnumerable<SerializableStockPrices> Acquire() {
	//		DateTime innerSince = _buff.Min(x => x.Date);
	//		DateTime innerUntil = _buff.Max(x => x.Date);
	//		IEnumerable<SerializableStockPrices> sin = Enumerable.Empty<SerializableStockPrices>();
	//		IEnumerable<SerializableStockPrices> utl = Enumerable.Empty<SerializableStockPrices>();
	//		if (_since < innerSince)
	//			sin = this.gData(_since, innerSince);
	//		if (innerUntil < _until)
	//			utl = this.gData(innerUntil, _until);

	//		if (!_buff.Intersect(sin).Any() || !_buff.Intersect(utl).Any())
	//			_buff = this.gData(innerSince, innerUntil);

	//		return _buff
	//			.Union(sin)
	//			.Union(utl)
	//			.OrderBy(x => x.Date)
	//			.ToArray();
	//	}
	//	readonly string codeUrl;
	//	readonly WebClient wc = new WebClient() { Encoding = Encoding.UTF8 };
	//	IEnumerable<SerializableStockPrices> gData(DateTime since, DateTime until) {
	//		string spanUrl = codeUrl + spanFormat(since, until);
	//		int page = 1;
	//		IEnumerable<SerializableStockPrices> data;
	//		while (gpData(spanUrl + page.ToString(), out data)) {
	//			foreach (var v in data) yield return v;
	//			page++;
	//		}
	//	}
	//	bool gpData(string uri, out IEnumerable<SerializableStockPrices> data) {
	//		var src = wc.DownloadString(uri);
	//		HtmlDocument doc = new HtmlDocument();
	//		doc.LoadHtml(src);
	//		var dd = doc.DocumentNode
	//			.SelectNodes(@"//div[@class=""padT12 marB10 clearFix""]")
	//			.Descendants("tr")
	//			.Select(x => new { Tds = x.Elements("td").Select(e => e.InnerText) })
	//			.Select(x => ResultWithValue.Of<IEnumerable<string>, SerializableStockPrices>(tryGet, x.Tds))
	//			.Where(x => x.Result)
	//			.Select(x => x.Value);
	//		data = dd;
	//		return dd.Any();
	//	}
		
	//	static bool tryGet(IEnumerable<string> ele,out SerializableStockPrices sp) {
	//		sp = null;
	//		int cnt = ele.Count();
	//		if (cnt < 5 || 7 < cnt) return false;
	//		var _sp = new SerializableStockPrices();
	//		if (!ResultWithValue.Of<DateTime>(DateTime.TryParse, ele.First())
	//			.TrueOrNot(dt => _sp.Date = dt))
	//			return false;
	//		var e = ele.Skip(1)
	//			.Select(x => ResultWithValue.Of<double>(double.TryParse, x))
	//			.Where(x => x.Result)
	//			.Select(x => x.Value);
	//		if (!e.Any()) return false;

	//		int ecnt = e.Count();
	//		switch (ecnt) {
	//		case 6: 
	//			_sp.AdjustedRate = e.ElementAt(3) / e.ElementAt(5);
	//			//_sp.AdjustedClosingPrice = e.ElementAt(5);
	//			goto case 5;
	//		case 5:
	//			_sp.Turnover = e.ElementAt(4);
	//			goto case 4;
	//		case 4:
	//			_sp.ClosingPrice = e.ElementAt(3);
	//			goto case 3;
	//		case 3:
	//			_sp.Low = e.ElementAt(2);
	//			goto case 2;
	//		case 2:
	//			_sp.High = e.ElementAt(1);
	//			goto case 1;
	//		case 1:
	//			_sp.OpeningPrice = e.ElementAt(0);
	//			goto case 0;
	//		case 0:
	//			sp = _sp;
	//			break;
	//		default:
	//			if (7 <= ecnt) goto case 6;
	//			return false;
	//		}
	//		return true;
	//	}

	//	static string spanFormat(DateTime since, DateTime until) {
	//		string ur = ".T&sy=" + since.Year.ToString() + "&sm=" + since.Month.ToString() + "&sd=" + since.Day.ToString()
	//			+ "&ey=" + until.Year.ToString() + "&em=" + until.Month.ToString() + "&ed=" + until.Day.ToString() + "&tm=d&p=";
	//		return ur;
	//	}
	//}

	///// <summary>Localファイルと対になるインスタンス。
	///// <para>各銘柄ごとの一連の株価を保有する。</para></summary>
	//public class Collector {
	//	private class PricesComparer : IEqualityComparer<SerializableStockPrices> {
	//		public bool Equals(SerializableStockPrices x, SerializableStockPrices y) {
	//			return object.Equals(x.Date, y.Date);
	//		}
	//		public int GetHashCode(SerializableStockPrices obj) {
	//			return obj.Date.GetHashCode();
	//		}
	//	}

	//	static string localPath = Local.CurrentPath + @"Yahoo" + Path.DirectorySeparatorChar;

	//	//internal StockPriceCollector(int tickerSymbol) : this(tickerSymbol,DataSource.Kdb) { }
	//	internal Collector(int tickerSymbol, DataSource src) {
	//		readTime = DateTime.Now;
	//		_tickerSymbol = tickerSymbol;
	//		//filePath = localPath + tickerSymbol.ToString() + ".xml";
	//		filePath = localPath + src.ToString() + Path.DirectorySeparatorChar + tickerSymbol.ToString() + ".xml";
	//		_src = src;
	//	}
	//	DataSource _src;
	//	DateTime readTime;
	//	int _tickerSymbol;
	//	public int TickerSymbol { get { return _tickerSymbol; } }
	//	readonly string filePath;

	//	IEnumerable<SerializableStockPrices> prices = Enumerable.Empty<SerializableStockPrices>();

	//	public IEnumerable<Prices> GetData(TimeScale scale, DateTime since) {
	//		return GetData(scale, since, DateTime.Now);
	//	}
	//	public IEnumerable<Prices> GetData(TimeScale scale, DateTime since, DateTime until) {
	//		//DateTimeの端数切り落とし
	//		since = new DateTime(since.Year, since.Month, since.Day);
	//		until = new DateTime(until.Year, since.Month, since.Day);

	//		if (!prices.Any() || since < prices.Min(x => x.Date) || prices.Max(x => x.Date) < until) {
	//			//ファイル読込み
	//			Prices[] pr;
	//			var deseri = new XmlSerializer(typeof(Prices[]));
	//			try {
	//				using (FileStream fs = new FileStream(this.filePath, FileMode.Open)) {
	//					pr = deseri.Deserialize(fs) as Prices[];
	//				}
	//			} catch {
	//				pr = new Prices[0];
	//			}
	//			//再評価
	//			if (!prices.Any() || since < prices.Min(x => x.Date) || prices.Max(x => x.Date) < until) {

	//				switch (_src) {
	//				//再取得、非重複項目ファイル書込み
	//				case DataSource.YahooScraping: //ヤフーから
	//					prices = YahooClient.Acquire(TickerSymbol, since, until, prices);
	//					goto WriteIO;
	//				case DataSource.Kdb: //K-dbから
	//					prices = KdbHtmlReader.Acquire(TickerSymbol, since, until, prices);
	//					goto WriteIO;
	//				WriteIO:
	//					//ここで書込み
	//					var seri = new XmlSerializer(typeof(Prices[]));
	//					try {
	//						using (FileStream fs = new FileStream(this.filePath, FileMode.Create)) {
	//							seri.Serialize(fs, this.prices.ToArray());
	//						}
	//					} catch { /*ignore*/ }
	//					break;
	//				default:
	//					break;
	//				}
	//			}
	//		}
	//		return _scaleMarge(
	//			prices
	//			.SkipWhile(x => x.Date < since)
	//			.TakeWhile(x => x.Date <= until)
	//			.Select(x => new Prices(x)), scale);
	//	}

	//	static IEnumerable<Prices> _scaleMarge(IEnumerable<Prices> priceData, TimeScale scale) {
	//		switch (scale) {
	//		case TimeScale.Weekly:
	//			priceData = _summarize(priceData.GroupBy(x => weeklyChunk(x.Date)));
	//			break;
	//		case TimeScale.Monthly:
	//			priceData = _summarize(priceData.GroupBy(x => monthlyChunk(x.Date)));
	//			break;
	//		case TimeScale.Yearly:
	//			priceData = _summarize(priceData.GroupBy(x => yearlyChunk(x.Date)));
	//			break;
	//		}
	//		return priceData;
	//	}
	//	static IEnumerable<Prices> _summarize(IEnumerable<IGrouping<DateTime, Prices>> pricesGroup) {
	//		var d = pricesGroup.Select(x => new SerializableStockPrices() {
	//			Date = x.First().Date,
	//			OpeningPrice = x.First().OpeningPrice,
	//			ClosingPrice = x.Last().ClosingPrice,
	//			High = x.Max(h => h.High),
	//			Low = x.Min(l => l.Low),
	//			Turnover = x.Sum(t => t.Turnover),
	//		});
	//		return d.Select(x => new Prices(x));
	//	}
	//	static DateTime weeklyChunk(DateTime dt) {
	//		var dow = dt.DayOfWeek;
	//		switch (dow) {
	//		case DayOfWeek.Monday:
	//			return dt;
	//		case DayOfWeek.Tuesday:
	//			return dt.AddDays(-1);
	//		case DayOfWeek.Wednesday:
	//			return dt.AddDays(-2);
	//		case DayOfWeek.Thursday:
	//			return dt.AddDays(-3);
	//		case DayOfWeek.Friday:
	//			return dt.AddDays(-4);
	//		case DayOfWeek.Saturday:
	//			return dt.AddDays(-5);
	//		case DayOfWeek.Sunday:
	//			return dt.AddDays(-6);
	//		default:
	//			return dt;
	//		}
	//	}
	//	static DateTime monthlyChunk(DateTime dt) { return new DateTime(dt.Year, dt.Month, 1); }
	//	static DateTime yearlyChunk(DateTime dt) { return new DateTime(dt.Year, 1, 1); }

	//}
}
