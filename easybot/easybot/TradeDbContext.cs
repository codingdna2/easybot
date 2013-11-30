using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace easybot
{
    [Table("btce_trades")]
    public class DbTrade
    {
        public int Now { get; set; }
        public DateTime Timestamp { get; set; }
        public decimal Price { get; set; }
        public decimal Amount { get; set; }
        [Key] public long tid { get; set; }

        public string Price_Currency { get; set; }
        public string Item { get; set; }
        public string Trade_Type { get; set; }
    }

    public class TradeDbContext : DbContext
    {
        public DbSet<DbTrade> BtceTrades { get; set; }
    }
}
