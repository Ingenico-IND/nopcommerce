namespace Nop.Web.Models.emandateSi
{
    public class Issuance
    {
        public string year { get; set; }
        public string month { get; set; }
        public string dateTime { get; set; }

        public Issuance()
        {
            year = "";
            month = "";
            dateTime = "";
        }
    }
}
