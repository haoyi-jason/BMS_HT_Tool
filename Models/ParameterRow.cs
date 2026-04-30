using System.ComponentModel;

namespace BmsHostUi.Models
{
    public sealed class ParameterRow : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public ushort Address { get; set; }
        private long _value;
        private string _valueText;
        public long Value
        {
            get => _value;
            set
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public string ValueText
        {
            get => _valueText;
            set
            {
                _valueText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValueText)));
            }
        }

        public string AddressHex => "0x" + Address.ToString("X4");

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
