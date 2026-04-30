namespace BmsHostUi.Models
{
    public sealed class RegisterDefinition
    {
        public string Name { get; set; }
        public ushort Address { get; set; }
        public string Unit { get; set; }
        public double Scale { get; set; } = 1.0;

        public string AddressHex => "0x" + Address.ToString("X4");
    }
}
