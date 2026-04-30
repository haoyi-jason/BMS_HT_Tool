using System;
using System.Threading;
using System.Threading.Tasks;

namespace BmsHostUi.Services
{
    public interface IModbusService
    {
        Task ConnectAsync(string portName, int baudRate, byte slaveId, CancellationToken cancellationToken);
        Task<ushort[]> ReadHoldingRegistersAsync(ushort startAddress, ushort count, CancellationToken cancellationToken);
        Task WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken cancellationToken);
        Task WriteDataFlashAccessKeyAsync(ushort accessKey, CancellationToken cancellationToken);
        Task<ushort> ReadDataFlashRegisterAsync(ushort dataFlashAddress, CancellationToken cancellationToken);
        Task<uint> ReadDataFlashRegister32Async(ushort dataFlashAddress, CancellationToken cancellationToken);
        Task WriteDataFlashRegisterAsync(ushort dataFlashAddress, ushort value, CancellationToken cancellationToken);
        Task WriteDataFlashRegister32Async(ushort dataFlashAddress, uint value, CancellationToken cancellationToken);
        Task DisconnectAsync();

        bool IsConnected { get; }
        event EventHandler<string> Log;
    }
}
