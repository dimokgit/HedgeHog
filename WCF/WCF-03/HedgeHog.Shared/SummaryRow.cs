using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared {
  class SummaryRow {
    string OfferID { get; set; }
    int DefaultSortOrder { get; set; }
    string Instrument { get; set; }
    double SellNetPL { get; set; }
    double SellAmountK { get; set; }
    double SellAvgOpen { get; set; }
    double BuyClose { get; set; }
    double SellClose { get; set; }
    double BuyAvgOpen { get; set; }
    double BuyAmountK { get; set; }
    double BuyNetPL { get; set; }
    double AmountK { get; set; }
    double GrossPL { get; set; }
    double NetPL { get; set; }
    double SellNetPLPip { get; set; }
  }
}
