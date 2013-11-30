using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace easybot
{
    public class OHLC
    {
        public DateTime Date { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Open { get; set; }
        public decimal Close { get; set; }
        public int TradesCount { get; set; }
        public TradeSource TradeSource { get; set; }
        public OHLC() { }

        public override string ToString()
        {
            return string.Format("{0} O:{1} H:{2} L:{3} C:{4}", Date, Open, High, Low, Close);
        }
    }

    public class MovingAverage
    {
        public int Period { get; set; }
        public int Begin { get; set; }
        public int Length { get; set; }
        public double[] Output { get; set; }
    }
}
