using Newtonsoft.Json.Linq;

namespace BtcE
{
	public class Funds
	{

        public decimal Btc { get; private set; }
        public decimal Ltc { get; private set; }
        public decimal Nmc { get; private set; }
        public decimal Nvc { get; private set; }
        public decimal Trc { get; private set; }
        public decimal Ppc { get; private set; }
        public decimal Ftc { get; private set; }
		public decimal Usd { get; private set; }
		public decimal Rur { get; private set; }
        public decimal Eur { get; private set; }

        public decimal GetBalance(string currency)
        {
            switch (currency)
            {
                case "BTC":
                    return this.Btc;

                case "LTC":
                    return this.Ltc;

                case "NMC":
                    return this.Nmc;

                case "TRC":
                    return this.Trc;

                case "PPC":
                    return this.Ppc;

                case "FTC":
                    return this.Ftc;

                case "USD":
                    return this.Usd;

                case "RUR":
                    return this.Rur;

                case "EUR":
                    return this.Eur;
            }
            return -0.01m;
        }

		public static Funds ReadFromJObject(JObject o) {
			if ( o == null )
				return null;
			return new Funds() {
                Btc = o.Value<decimal>("btc"),
                Ltc = o.Value<decimal>("ltc"),
                Nmc = o.Value<decimal>("ntc"),
                Nvc = o.Value<decimal>("nvc"),
                Trc = o.Value<decimal>("trc"),
                Ppc = o.Value<decimal>("ppc"),
                Ftc = o.Value<decimal>("Ftc"),
                Usd = o.Value<decimal>("Usd"),
                Rur = o.Value<decimal>("rur"),
                Eur = o.Value<decimal>("eur")
			};
		}
	};

}
