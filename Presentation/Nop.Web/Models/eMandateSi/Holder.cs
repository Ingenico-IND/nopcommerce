namespace Nop.Web.Models.emandateSi
{
    public class Holder
    {
        public string name { get; set; }
        public Address address { get; set; }

        public Holder()
        {
            name = "";
            address = new Address();
        }
    }
}
