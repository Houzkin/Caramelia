using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Calamelia.StockPrice { 
	using Calamelia.StockPrice.Serialize;
	using Calamelia.StockPrice.DataClient;
	using System.Collections.ObjectModel;
	using System.Threading;
	/// <summary>チャートのスケールを表す。</summary>
	public enum TimeScale {
		Daily,Weekly,Monthly,Quarter,Yearly,
	}
	/// <summary>株価データの取得元を表す。</summary>
	public enum DataSource {
		Yahoo,
		Kdb,
		KabuDotCom,
	}
	/// <summary>通貨ペア</summary>
	public enum FXPair {
		/// <summary>ドル円</summary>
		USDJPY,
		/// <summary>ユーロ円</summary>
		EURJPY,
		/// <summary>ポンド円</summary>
		GBPJPY,
		/// <summary>カナダドル円</summary>
		CADJPY,
		/// <summary>スイスフラン円</summary>
		CHFJPY,
		/// <summary>オーストラリアドル円</summary>
		AUDJPY,
		/// <summary>ニュージーランドドル円</summary>
		NZDJPY,
		/// <summary>トルコリラ円</summary>
		TRYJPY,
		/// <summary>南アランド円</summary>
		ZARJPY,
	}
	/// <summary>表示用の各値を提供する。</summary>
	public class Prices {
		readonly SerializablePrices sp;
		internal Prices(SerializablePrices stockPrices) {
			sp = stockPrices;
		}
		double AdjustedRate {
			get { return sp.AdjustedRate != 0 ? sp.AdjustedRate : 1; }
		}

		public int TickerSymbol { get { return sp.TickerSymbol; } }
		public string SymbolName { get { return sp.SymbolName; } }
		public string Market { get { return sp.Market; } }

		public DateTime Date { get { return sp.Date; } }
		public double OpeningPrice { get { return sp.OpeningPrice / AdjustedRate; } }
		public double ClosingPrice { get { return sp.ClosingPrice / AdjustedRate; } }
		public double High { get { return sp.High / AdjustedRate; } }
		public double Low { get { return sp.Low / AdjustedRate; } }
		public double Turnover { get { return sp.Turnover * AdjustedRate; } }
	}
	
	/// <summary>各銘柄を管理、提供する。</summary>
	public class StockPriceManager {
		
		public static void Update(params DataSource[] src) {
			if(src == null || !src.Any()) {
				HashSet<DataSource> srcl = new HashSet<DataSource>();
				foreach (DataSource ds in Enum.GetValues(typeof(DataSource))) srcl.Add(ds);
				src = srcl.ToArray();
			}
			if (src.Contains(DataSource.Kdb)) {
				KdbClient.Update();
			}
			if (src.Contains(DataSource.KabuDotCom)) {
				KabuDotComClient.Update();
			}
			if (src.Contains(DataSource.Yahoo)) {
			}
		}
		public static void UpdateAdjustSheet() {
			Update(DataSource.KabuDotCom);
		}
		internal static StockPriceManager SetMessage(ManagementMessage cmm) {
			return new StockPriceManager(cmm);
		}
		internal static StockPriceManager SetMessage(DataSource sender, string message) { return SetMessage(sender, message, ""); }
		static StockPriceManager SetMessage(DataSource sender, string message, string detail) {
			return new StockPriceManager(new ManagementMessage() { Sender = sender, Signal = MessageSignal.Status, Message = message, Detail = detail });
		}
		//public static IReadOnlyList<ManagementMessage> Messages { get { return messages; } }
		public static ReadOnlyObservableCollection<ManagementMessage> Messages {
			get {
				if (readOnlyMsg == null) readOnlyMsg = new ReadOnlyObservableCollection<ManagementMessage>(messages);
				return readOnlyMsg;
			}
		}
		static ReadOnlyObservableCollection<ManagementMessage> readOnlyMsg;
		static ObservableCollection<ManagementMessage> messages {
			get {
				if (_messages != null) return _messages;
				_messages = new ObservableCollection<ManagementMessage>();
				_messages.CollectionChanged += (o, e) => {
					if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add) 
						ManagementStatusStream(null, e.NewItems.OfType<ManagementMessage>().First());
				};
				return _messages;
			}
		}
		static ObservableCollection<ManagementMessage> _messages;
		public static event EventHandler<ManagementMessage> ManagementStatusStream;

		StockPriceManager(ManagementMessage mm) {
			_mm = mm;
			messages.Add(_mm);
		}
		ManagementMessage _mm;
		internal StockPriceManager ClearAndSet(MessageSignal signal, string message,string detail = "") {
			return ClearAndSet(new ManagementMessage() { Sender = _mm.Sender, Signal = signal, Message = message, Detail = detail });
		}
		internal StockPriceManager ClearAndSet(string message,string detail = "") {
			return ClearAndSet(_mm.Signal, message, detail);
		}
		StockPriceManager ClearAndSet(ManagementMessage cmm) {
			messages.Remove(_mm);
			return new StockPriceManager(cmm);
		}
	}
	public enum MessageSignal { Error, Status}
	public class ManagementMessage {
		public DataSource Sender { get; internal set; }
		public string Message { get; internal set; }
		public MessageSignal Signal { get; internal set; }
		public string Detail { get; internal set; }
		public override string ToString() {
			return "Sender:" + Sender + ", IsError:" + Signal + ", Message:" + Message + ", Detail:" + Detail;
		}
	}
	public class ManagementMessageEventArgs : EventArgs {
		ManagementMessage cmmSrc { get; set; }
		internal ManagementMessageEventArgs(ManagementMessage cmm) {
			cmmSrc = cmm;
		}
		public DataSource Sender { get { return cmmSrc.Sender; } }
		public MessageSignal IsError { get { return cmmSrc.Signal; } }
		public string Message { get { return cmmSrc.Message; } }
		public string Detail { get { return cmmSrc.Detail; } }
	}
	class STATask {
		public static Task<T> Run<T>(Func<T> func) {
			var tcs = new TaskCompletionSource<T>();
			var thread = new Thread(() => {
				try {
					tcs.SetResult(func());
				} catch (Exception e) {
					tcs.SetException(e);
				}
			});
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
			return tcs.Task;
		}
		public static Task Run(Action act) {
			return Run(() => {
				act();
				return true;
			});
		}
	}
}
/*
 * url : http://mujinzou.com/
 * url : http://www.geocities.co.jp/WallStreet-Stock/9256/data.html
 * url : http://softreed.la.coocan.jp/data.htm
 * 圧縮形式 : zip
 * 
 * url : http://stockinvestinfo.web.fc2.com/stockdatadownload.html
 * 圧縮形式 : rar
 * 
 * url : http://www.edatalab.net/kabu/
 * 圧縮形式 : lzh
 * 
 * url : http://hesonogoma.com/stocks/japan-all-stock-data.html
 * scraping
 * 
 * 
 * ref:
 * http://hqac.hatenadiary.com/archive/category/XBRL
 * http://qiita.com/shima_x/items/58634a838ab37c3607b5
 * http://www.kanzaki.com/docs/xml/xlink.html
 * http://www.kanzaki.com/docs/sw/names.html
 * */
