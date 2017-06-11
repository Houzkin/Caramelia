using Houzkin;
using Calamelia.StockPrice.Serialize;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using System.Net;
using CsvHelper;
using Caramelia;

namespace Calamelia.StockPrice.DataClient {
	
	enum KdbData { stocks,indices,}
	internal static class KdbClient {
		static string localPath = Local.CurrentPath + "K-db" + Path.DirectorySeparatorChar;// + "stocks" + Path.DirectorySeparatorChar;
		static DateTime sinceMin = new DateTime(2016,5,25);// new DateTime(2007, 1, 4);
		internal static void Delete() {
			if (Directory.Exists(localPath)) Directory.Delete(localPath);
		}
		internal static void Update() {
			foreach (KdbData en in Enum.GetValues(typeof(KdbData))) {
				IEnumerable<Tuple<DateTime, IEnumerable<SerializablePrices>>> srcs 
					= Enumerable.Empty<Tuple<DateTime, IEnumerable<SerializablePrices>>>();
				var today = DateTime.Today.AddDays(-1);
				try {
					var sdate = getExistingDataDate(en);
					if (getExistingDataDate(en).Any()) {
						var localMin = getExistingDataDate(en).Min();
						var localMax = getExistingDataDate(en).Max();
						if (sinceMin < localMin) srcs = srcs.Union(download(localMin.AddDays(-1), sinceMin, en));
						if (localMax < today) srcs = srcs.Union(download(localMax.AddDays(1), today, en));
					} else {
						srcs = download(sinceMin, today, en);
					}
					foreach (var kvp in srcs) {
						add(kvp.Item1, kvp.Item2.ToArray(), en);
					}
				} catch (WebException we) {
					StockPriceManager.SetMessage(
						new ManagementMessage() {
							Sender = DataSource.Kdb,
							Signal = MessageSignal.Error,
							Message = en.ToString() + " 接続エラー発生",
							Detail = we.Message,
						});
				} catch (CsvHelperException ce) {
					StockPriceManager.SetMessage(
						new ManagementMessage() {
							Sender = DataSource.Kdb,
							Signal = MessageSignal.Error,
							Message = en.ToString() +" 解析中にエラー発生",
							Detail =  ce.Message,
						});
				}
			}
		}
		
		internal static IEnumerable<SerializablePrices> Acquire(KdbData kdb, DateTime since,DateTime until,Func<SerializablePrices,bool> pred) {
			var span = getExistingDataDate(kdb).SkipWhile(a => a < since).TakeWhile(a => a <= until);
			foreach(var dt in span) {
				var d = tryGet(dt, kdb).OfType<SerializablePrices>()
					.Where(a => pred(a));
				foreach (var c in d) yield return c;
			}
		}
		internal static IEnumerable<DateTime> GetTimeLineScale() { return getExistingDataDate(KdbData.stocks); }
		#region download
		
		static IEnumerable<Tuple<DateTime,IEnumerable<SerializablePrices>>> download(DateTime start, DateTime end,KdbData type) {

			Func<DateTime, DateTime, DateTime> nxt = (c, e) => {
				return (c < e) ? c.AddDays(1) : (c > e) ? c.AddDays(-1) : c;
			};
			DateTime current = start;
			do {
				if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday) {
					Thread.Sleep(2000);
					var s = _download(current, type);
					StockPriceManager.SetMessage(
						DataSource.Kdb,
						type.ToString() + ", " + current.ToString("yyyy-MM-dd") + ", ダウンロード完了"
					);
					if (!string.IsNullOrEmpty(s)) {
						using (var str = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(s))))
						using (var csv = new CsvReader(str)) {
							switch (type) {
							case KdbData.stocks:
								csv.Configuration.RegisterClassMap<StockCsvMap>();
								break;
							case KdbData.indices:
								csv.Configuration.RegisterClassMap<IndexCsvMap>();
								break;
							}
							var rec = csv.GetRecords<SerializablePrices>().ToArray();
							foreach (var r in rec) r.Date = current;
							yield return new Tuple<DateTime, IEnumerable<SerializablePrices>>(current, rec);
						}
					}
					Thread.Sleep(3000);
				}
				current = nxt(current, end);
			} while (current != end);
		}

		//WebClientを作成
		static WebClient wc = new WebClient() { Encoding = Encoding.Default };
		static string _download(DateTime dt,KdbData type) {
			//try {
				//ダウンロード元のURL
				//string url = "http://k-db.com/stocks/" + dt.ToString("yyyy-MM-dd") + "?download=csv";
				string url = "http://k-db.com/" + type.ToString() + "/" + dt.ToString("yyyy-MM-dd") + "?download=csv";
				//データを文字列としてダウンロードする
				return wc.DownloadString(url);
			//} catch {
			//	return "";
			//}
		}
		#endregion

		#region IO
		static IEnumerable<DateTime> getExistingDataDate(KdbData type) {
			try {
				return Directory.GetFiles(localPath + type.ToString() + Path.DirectorySeparatorChar, "*", SearchOption.AllDirectories)
					.Select(a=>Path.GetFileNameWithoutExtension(a))
					.Select(a => ResultWithValue.Of<string, DateTime>(DateTime.TryParse, a))
					.Where(a => a.Result)
					.Select(a => a.Value)
					.OrderBy(a => a);
			} catch (DirectoryNotFoundException) {
				return new DateTime[0];
			}
		}
		static string createFileName(DateTime date,KdbData type) {
			return localPath + type.ToString() + Path.DirectorySeparatorChar + date.Year.ToString() + Path.DirectorySeparatorChar + date.ToString("yyyy-MM-dd") + ".xml";
		}
		static void createDirectoryIfNotFound(DateTime date, KdbData type) {
			string s = localPath + type.ToString() + Path.DirectorySeparatorChar + date.Year.ToString();
			if (!Directory.Exists(s)) Directory.CreateDirectory(s);
		}
		static void add(DateTime date, IEnumerable<SerializablePrices> table,KdbData type) {
			if (!table.Any()) return;
			createDirectoryIfNotFound(date, type);
			string targetPath = createFileName(date, type);
			try {
				var seri = new XmlSerializer(typeof(SerializablePrices[]));
				using (FileStream fs = new FileStream(targetPath, FileMode.Create)) {
					seri.Serialize(fs, table.ToArray());
				}
				StockPriceManager.SetMessage(
					DataSource.Kdb,
					type.ToString() + ", " + date.ToString("yyyy-MM-dd") + ", 書き込み完了"
				);
			} catch (Exception e) {
				StockPriceManager.SetMessage(new ManagementMessage() {
					Sender = DataSource.Kdb,
					Signal = MessageSignal.Error,
					Message = type.ToString() + " " + date.ToString("yyyy-MM-dd") + " のデータの書き込みに失敗しました。",
					Detail = e.Message,
				});
			}
		}
		static SerializablePrices[] tryGet(DateTime date, KdbData type) {
			if (!getExistingDataDate(type).Any(a => a == date)) return new SerializablePrices[0];
			try {
				var deseri = new XmlSerializer(typeof(SerializablePrices[]));
				using (FileStream fs = new FileStream(createFileName(date, type), FileMode.Open)) {
					return deseri.Deserialize(fs) as SerializablePrices[];
				}
			}catch(Exception e){
				StockPriceManager.SetMessage(new ManagementMessage() {
					Sender = DataSource.Kdb,
					Signal = MessageSignal.Error,
					Message = type.ToString() + " " + date.ToString("yyyy-MM-dd") + " のデータの読み込みに失敗しました。",
					Detail = e.Message
				});
				return new SerializablePrices[0];
			}
		}
		#endregion IO
	}
	public class StockCsvMap : CsvClassMap<SerializablePrices> {
		public StockCsvMap() {
			Map(m => m.TickerSymbol).Index(0).TypeConverter<TickerCodeConv>();
			Map(m => m.SymbolName).Index(1);
			Map(m => m.Market).Index(2).Default("Unknown");
			Map(m => m.OpeningPrice).Index(3).Default(0);
			Map(m => m.High).Index(4).Default(0);
			Map(m => m.Low).Index(5).Default(0);
			Map(m => m.ClosingPrice).Index(6).Default(0);
			Map(m => m.Turnover).Index(7).Default(0);
		}
	}
	public class TickerCodeConv : DefaultTypeConverter {
		public override bool CanConvertFrom(Type type) {
			return type == typeof(string);
		}
		public override object ConvertFromString(TypeConverterOptions options, string text) {
			var s = text.Split('-').FirstOrDefault();
			var numberStyle = options.NumberStyle ?? System.Globalization.NumberStyles.Integer;
			int i;
			if (int.TryParse(s, numberStyle, options.CultureInfo, out i)) {
				return i;
			}
			return 0;
		}
	}
	public class IndexCsvMap : CsvClassMap<SerializablePrices> {
		public IndexCsvMap() {
			Map(m => m.SymbolName).Index(0);
			Map(m => m.OpeningPrice).Index(1);
			Map(m => m.High).Index(2);
			Map(m => m.Low).Index(3);
			Map(m => m.ClosingPrice).Index(4);
		}
	}
}
