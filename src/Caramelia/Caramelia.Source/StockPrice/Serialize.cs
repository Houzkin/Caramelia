using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Calamelia.StockPrice.Serialize {
	//public class SerializablePrices {
	//}
	public class SerializablePrices {
		public SerializablePrices() { AdjustedRate = 1; }
		public int TickerSymbol { get; set; }

		//[XmlIgnore]
		public double AdjustedRate { get; set; }
		public string SymbolName { get; set; }
		public string Market { get; set; }

		public double OpeningPrice { get; set; }
		public double ClosingPrice { get; set; }
		public double High { get; set; }
		public double Low { get; set; }
		DateTime _date;
		public DateTime Date {
			get { return _date; }
			set { _date = new DateTime(value.Year, value.Month, value.Day); }
		}

		public double Turnover { get; set; }
		//public double AdjustedClosingPrice { get; set; }

		public SerializablePrices Clone() {
			return (SerializablePrices)this.MemberwiseClone();
		}
		public override bool Equals(object obj) {
			var ob = obj as SerializablePrices;
			if (ob == null) return false;
			if (
				this.Date == ob.Date &&
				this.OpeningPrice == ob.OpeningPrice &&
				this.ClosingPrice == ob.ClosingPrice &&
				this.High == ob.High &&
				this.Low == ob.Low &&
				this.Turnover == ob.Turnover) return true;
			return false;
		}
		public override int GetHashCode() {
			return Tuple.Create(this.Date, this.OpeningPrice, this.ClosingPrice, this.High, this.Low, this.Turnover).GetHashCode();
		}
	}

	public class StockSplitInfo {

		public int TickerSymbol { get; set; }
		public string SymbolName { get; set; }
		/// <summary>分割比率</summary>
		public double SplitRate { get; set; }
		/// <summary>権利確定日</summary>
		public DateTime DateOfRightAllotment { get; set; }
		/// <summary>権利付き最終日</summary>
		public DateTime RightWithTheLastDate { get; set; }
		/// <summary>効力発生日</summary>
		public DateTime EffectiveDate { get; set; }
		/// <summary>売却可能日</summary>
		public DateTime AvailableForSaleDate { get; set; }

		public override bool Equals(object obj) {
			var ob = obj as StockSplitInfo;
			if (ob == null) return false;
			//return asTuple(this) == asTuple(ob);
			return this.TickerSymbol == ob.TickerSymbol &&
				this.SymbolName == ob.SymbolName &&
				this.SplitRate == ob.SplitRate &&
				this.DateOfRightAllotment == ob.DateOfRightAllotment &&
				this.RightWithTheLastDate == ob.RightWithTheLastDate &&
				this.EffectiveDate == ob.EffectiveDate &&
				this.AvailableForSaleDate == ob.AvailableForSaleDate;
		}
		public override int GetHashCode() {
			return asTuple(this).GetHashCode();
		}
		static Tuple<int, string, double, DateTime, DateTime, DateTime, DateTime> asTuple(StockSplitInfo info) {
			return new Tuple<int, string, double, DateTime, DateTime, DateTime, DateTime>(
				info.TickerSymbol,
				info.SymbolName,
				info.SplitRate,
				info.DateOfRightAllotment,
				info.RightWithTheLastDate,
				info.EffectiveDate,
				info.AvailableForSaleDate);
		}
	}
}
