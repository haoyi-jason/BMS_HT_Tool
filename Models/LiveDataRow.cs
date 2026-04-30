using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BmsHostUi.Models
{
    public sealed class LiveDataRow : INotifyPropertyChanged
    {
        private string _name;
        private string _addressHex;
        private string _valueText;
        private string _valueType;
        private long _rawValue;
        private string _unit;
        private DateTime _timestamp;

        public string Name
        {
            get { return _name; }
            set
            {
                if (_name == value)
                {
                    return;
                }

                _name = value;
                OnPropertyChanged();
            }
        }

        public string AddressHex
        {
            get { return _addressHex; }
            set
            {
                if (_addressHex == value)
                {
                    return;
                }

                _addressHex = value;
                OnPropertyChanged();
            }
        }

        public string ValueText
        {
            get { return _valueText; }
            set
            {
                if (_valueText == value)
                {
                    return;
                }

                _valueText = value;
                OnPropertyChanged();
            }
        }

        public string ValueType
        {
            get { return _valueType; }
            set
            {
                if (_valueType == value)
                {
                    return;
                }

                _valueType = value;
                OnPropertyChanged();
            }
        }

        public long RawValue
        {
            get { return _rawValue; }
            set
            {
                if (_rawValue == value)
                {
                    return;
                }

                _rawValue = value;
                OnPropertyChanged();
            }
        }

        public string Unit
        {
            get { return _unit; }
            set
            {
                if (_unit == value)
                {
                    return;
                }

                _unit = value;
                OnPropertyChanged();
            }
        }

        public DateTime Timestamp
        {
            get { return _timestamp; }
            set
            {
                if (_timestamp == value)
                {
                    return;
                }

                _timestamp = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TimestampText));
            }
        }

        public string TimestampText => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
