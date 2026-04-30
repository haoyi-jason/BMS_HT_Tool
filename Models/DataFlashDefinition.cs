namespace BmsHostUi.Models
{
    public sealed class DataFlashDefinition
    {
        public string Symbol { get; set; }
        public ushort Address { get; set; }
        public string Type { get; set; }
        public long DefaultValue { get; set; }
    }
}
