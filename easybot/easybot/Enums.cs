using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace easybot
{
    public enum TradeSource
    {
        NotSet,
        BitcoinCharts,
        Database,
        BTCe,
        CSV,
    }

    public enum MessageType
    {
        Message,
        Warning,
        Error
    }
}
