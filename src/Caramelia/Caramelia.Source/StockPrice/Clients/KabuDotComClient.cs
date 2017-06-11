using Houzkin;
using Calamelia.StockPrice.Serialize;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using System.ComponentModel;
using Caramelia;

namespace Calamelia.StockPrice.DataClient {
	internal static class KabuDotComClient {

		internal static void Update() {
			//var dld = download().ToArray();
			//if(dld.Any()) save(dld);
			downloadAndSave();
		}
		internal static IEnumerable<StockSplitInfo> Acquire(Func<StockSplitInfo,bool> pred) {
			return tryGet(pred,splitType.split,splitType.consolide);
		}

		static void downloadAndSave() {
			save(_download(splitType.split).ToArray(),splitType.split);
			save(_download(splitType.consolide).ToArray(),splitType.consolide);
		}
		static readonly string localPath = Local.CurrentPath + "KabuDotCom" + Path.DirectorySeparatorChar + "SplitInfo" + Path.DirectorySeparatorChar;
		
		enum splitType {split,consolide,}
		private static string toFileName(this splitType _self) {
			if (_self == splitType.split) return "StockSplitInfo.xml";
			else return "StockConsolidationInfo.xml";
		}
		private static string toDisplayName(this splitType _self) {
			if (_self == splitType.split) return "株式分割";
			else return "株式併合";
		}
		private static string getUrl(this splitType _self) {
			if (_self == splitType.split) return "http://kabu.com/investment/meigara/bunkatu.html";
			else return "http://kabu.com/investment/meigara/gensi.html";
		}
		#region IO
		static void save(StockSplitInfo[] infos,splitType filename) {
			var data = tryGet(a => true, filename);
			var ex = infos.Except(data);
			if (!ex.Any()) {
				StockPriceManager.SetMessage(DataSource.KabuDotCom, "新規データが存在しなかったため書き込み処理をスキップしました。");
				return;
			}

			if (!Directory.Exists(localPath)) Directory.CreateDirectory(localPath);
			string tgtPath = localPath + filename.toFileName();
			try {
				using(FileStream f = new FileStream(tgtPath, FileMode.Create)) {
					var seri = new XmlSerializer(typeof(StockSplitInfo[]));
					seri.Serialize(f, data.Union(infos).ToArray());
					StockPriceManager.SetMessage(DataSource.KabuDotCom, filename.toDisplayName() + "データを保存しました。");
				}
			} catch(Exception e) {
				StockPriceManager.SetMessage(new ManagementMessage() {
					Sender = DataSource.KabuDotCom, 
					Signal = MessageSignal.Error,
					Message = "書き込み処理に失敗しました。",
					Detail = e.Message,
				});
			}
		}
		static StockSplitInfo[] tryGet(Func<StockSplitInfo,bool> pred,params splitType[] type) {
			if (!Directory.Exists(localPath)) return new StockSplitInfo[0];
			IEnumerable<StockSplitInfo> buff = Enumerable.Empty<StockSplitInfo>();
			var deseri = new XmlSerializer(typeof(StockSplitInfo[]));
			if(type.Contains(splitType.split) && File.Exists(localPath + splitType.split.toFileName())) {
				using (var fs = new FileStream(localPath + splitType.split.toFileName(), FileMode.Open)) {
					buff = buff.Union((deseri.Deserialize(fs) as StockSplitInfo[]).Where(pred));
				}
			}else if(type.Contains(splitType.consolide) && File.Exists(localPath + splitType.consolide.toFileName())) {
				using (var fs = new FileStream(localPath + splitType.consolide.toFileName(), FileMode.Open)) {
					buff = buff.Union((deseri.Deserialize(fs) as StockSplitInfo[]).Where(pred));
				}
			}
			return buff.ToArray();
		}
		#endregion
		#region download
		static IEnumerable<StockSplitInfo> _download(splitType type) {
			string str = "";
			try {
				str = getSource(type.getUrl()).Result;
				StockPriceManager.SetMessage(DataSource.KabuDotCom, type.toDisplayName() + "データのダウンロード完了");
			} catch(Exception e) {
				StockPriceManager.SetMessage(new ManagementMessage() {
					Sender = DataSource.KabuDotCom,
					Signal = MessageSignal.Error,
					Message = "接続エラー",
					Detail = e.Message,
				});
				return new StockSplitInfo[0];
			}
			HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
			doc.LoadHtml(str);
			try {
				var dd = doc.DocumentNode
					//.SelectNodes(@"table[@class=""tbl01""]");
					.SelectNodes(@"//table[@class='tbl01']")
					.Descendants("tr")
					.Select(a => new { Prm = a.Elements("td").Select(e => e.InnerText) })
					//.Select(a => ResultWithValue.Of<IEnumerable<string>, StockSplitInfo>(tryParse, a.Prm))
					//.Where(a => a.Result)
					//.Select(a => a.Value);
					.Where(a=>a.Prm.Any())
					.Select(a => parse(a.Prm));
				StockPriceManager.SetMessage(DataSource.KabuDotCom, type.toDisplayName() + "データの解析完了");
				return dd;
			}catch(Exception e) {
				StockPriceManager.SetMessage(new ManagementMessage() {
					Sender = DataSource.KabuDotCom,
					Signal = MessageSignal.Error,
					Message = "解析エラー",
					Detail = e.Message,
				});
				return new StockSplitInfo[0];
			}
		}
		static async Task<string> getSource(string url) {
			return await STATask.Run(() => callWeb(url));
		}
		static string callWeb(string url) {
			var wb = new NonDispBrowser();
			wb.NavigateAndWait(url);
			var x = wb.Document.Body.InnerHtml;
			return x;
		}
		static StockSplitInfo parse(IEnumerable<string> ele) {
			if(ele.Count() >= 7) {
				return parseS(ele);
			}else if(ele.Count() <= 6) {
				return parseC(ele);
			}
			throw new NotSupportedException("無効なデータを取得した可能性があります。");
		}
		static StockSplitInfo parseS(IEnumerable<string> ele) {
			var info = new StockSplitInfo();
			info.DateOfRightAllotment = ResultWithValue.Of<DateTime>(DateTime.TryParse, ele.ElementAt(0)).Value;
			info.TickerSymbol = ResultWithValue.Of<int>(int.TryParse, ele.ElementAt(1)).Value;
			info.SymbolName = ele.ElementAt(2);
			info.SplitRate = parseSRate(ele.ElementAt(3));
			info.RightWithTheLastDate = ResultWithValue.Of<DateTime>(DateTime.TryParse, ele.ElementAt(4)).Value;
			info.EffectiveDate = ResultWithValue.Of<DateTime>(DateTime.TryParse, ele.ElementAt(5)).Value;
			info.AvailableForSaleDate = ResultWithValue.Of<DateTime>(DateTime.TryParse, ele.ElementAt(6)).Value;
			return info;
		}
		static StockSplitInfo parseC(IEnumerable<string> ele) {
			var info = new StockSplitInfo();
			info.EffectiveDate = ResultWithValue.Of<DateTime>(DateTime.TryParse, ele.ElementAt(0)).Value;
			info.TickerSymbol = ResultWithValue.Of<int>(int.TryParse, ele.ElementAt(1)).Value;
			info.SymbolName = ele.ElementAt(2);
			info.SplitRate = parseCRate(ele.ElementAt(3));
			info.RightWithTheLastDate = ResultWithValue.Of<DateTime>(DateTime.TryParse, ele.ElementAt(4)).Value;
			info.AvailableForSaleDate = info.RightWithTheLastDate.AddDays(1);
			return info;
		}
		static double parseSRate(string str) {
			var strs = str.Split('：')
				.Select(a=>a.Trim())
				.Select(a => double.Parse(a));
			return strs.ElementAt(1) / strs.ElementAt(0);
		}
		static double parseCRate(string str) {
			var strs = str.Replace("株", "").Split('→').Select(a => a.Trim()).Select(a => double.Parse(a));
			return strs.ElementAt(1) / strs.ElementAt(0);
		}
		#endregion
	}
	internal class NonDispBrowser : WebBrowser {
		bool done;
		// タイムアウト時間（10秒）
		TimeSpan timeout = new TimeSpan(0, 0, 10);
		protected override void OnDocumentCompleted(WebBrowserDocumentCompletedEventArgs e) {
			// ページにフレームが含まれる場合にはフレームごとに
			// このメソッドが実行されるため実際のURLを確認する
			if (e.Url == this.Url) done = true;
		}
		// ポップアップ・ウィンドウをキャンセル
		protected override void OnNewWindow(CancelEventArgs e) {
			e.Cancel = true;
		}
		public NonDispBrowser() {
			// スクリプト・エラーを表示しない
			this.ScriptErrorsSuppressed = true;
		}
		public bool NavigateAndWait(string url) {
			base.Navigate(url); // ページの移動
			done = false;
			DateTime start = DateTime.Now;
			while (done == false) {
				if (DateTime.Now - start > timeout) {
					// タイムアウト
					return false;
				}
				Application.DoEvents();
			}
			return true;
		}
	}
}
