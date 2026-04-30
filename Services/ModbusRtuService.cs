using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BmsHostUi.Services
{
    public sealed class ModbusRtuService : IModbusService
    {
        private const int SerialTimeoutMs = 3000;
        private const int RetryCount = 3;
        private const int RetryDelayMs = 80;
        private const int DfCommandDelayMs = 20;

        private const byte FcReadHolding = 0x03;
        private const byte FcWriteSingle = 0x06;

        private const ushort DfAccessKeyRegister = 99;
        private const ushort DfCommandRegRegister = 100;
        private const ushort DfCommandArgRegister = 101;
        private const ushort DfCommandArg2Register = 102;
        private const ushort DfCommandExecRegister = 98;
        private readonly object _ioLock = new object();
        private SerialPort _serialPort;
        private byte _slaveId;

        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;
        public event EventHandler<string> Log;

        public Task ConnectAsync(string portName, int baudRate, byte slaveId, CancellationToken cancellationToken)
        {
            if (IsConnected)
            {
                return Task.CompletedTask;
            }

            _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = SerialTimeoutMs,
                WriteTimeout = SerialTimeoutMs,
                DtrEnable = false,
                RtsEnable = false
            };
            _serialPort.Open();
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();
            _slaveId = slaveId;
            Log?.Invoke(this, "Connected: " + portName + " @ " + baudRate);
            return Task.CompletedTask;
        }

        public Task<ushort[]> ReadHoldingRegistersAsync(ushort startAddress, ushort count, CancellationToken cancellationToken)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Serial port is not connected.");
            }

            if (count == 0 || count > 125)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be 1..125.");
            }

            return Task.Run(() =>
            {
                var request = BuildReadHoldingRequest(startAddress, count);
                var response = Exchange(request, cancellationToken);

                if (response[1] == (byte)(FcReadHolding | 0x80))
                {
                    throw new InvalidOperationException("Modbus exception FC03: " + response[2]);
                }

                if (response[1] != FcReadHolding)
                {
                    throw new InvalidOperationException("Unexpected function code in FC03 response.");
                }

                var byteCount = response[2];
                if (byteCount != count * 2)
                {
                    throw new InvalidOperationException("Unexpected byte count in FC03 response.");
                }

                var values = new ushort[count];
                for (int i = 0; i < count; i++)
                {
                    int offset = 3 + i * 2;
                    values[i] = (ushort)((response[offset] << 8) | response[offset + 1]);
                }

                return values;
            }, cancellationToken);
        }

        public Task WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken cancellationToken)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Serial port is not connected.");
            }

            return Task.Run(() =>
            {
                var request = BuildWriteSingleRequest(address, value);
                var response = Exchange(request, cancellationToken);

                if (response[1] == (byte)(FcWriteSingle | 0x80))
                {
                    throw new InvalidOperationException("Modbus exception FC06: " + response[2]);
                }

                if (response.Length != 8 || !request.Take(6).SequenceEqual(response.Take(6)))
                {
                    throw new InvalidOperationException("Invalid FC06 echo response.");
                }

                Log?.Invoke(this, "Write single register: addr=" + address + ", value=" + value + ", slave=" + _slaveId);
            }, cancellationToken);
        }

        public Task WriteDataFlashAccessKeyAsync(ushort accessKey, CancellationToken cancellationToken)
        {
            return WriteSingleRegisterAsync(DfAccessKeyRegister, accessKey, cancellationToken);
        }

        public async Task<ushort> ReadDataFlashRegisterAsync(ushort dataFlashAddress, CancellationToken cancellationToken)
        {
            await WriteSingleRegisterAsync(DfCommandRegRegister, dataFlashAddress, cancellationToken).ConfigureAwait(false);
            await WriteSingleRegisterAsync(DfCommandExecRegister, 1, cancellationToken).ConfigureAwait(false);
            await Task.Delay(DfCommandDelayMs, cancellationToken).ConfigureAwait(false);

            var response = await ReadHoldingRegistersAsync(DfCommandArgRegister, 1, cancellationToken).ConfigureAwait(false);
            return response[0];
        }

        public async Task<uint> ReadDataFlashRegister32Async(ushort dataFlashAddress, CancellationToken cancellationToken)
        {
            await WriteSingleRegisterAsync(DfCommandRegRegister, dataFlashAddress, cancellationToken).ConfigureAwait(false);
            await WriteSingleRegisterAsync(DfCommandExecRegister, 1, cancellationToken).ConfigureAwait(false);
            await Task.Delay(DfCommandDelayMs, cancellationToken).ConfigureAwait(false);

            var response = await ReadHoldingRegistersAsync(DfCommandArgRegister, 2, cancellationToken).ConfigureAwait(false);
            return (uint)response[0] | ((uint)response[1] << 16);
        }

        public async Task WriteDataFlashRegisterAsync(ushort dataFlashAddress, ushort value, CancellationToken cancellationToken)
        {
            await WriteSingleRegisterAsync(DfCommandRegRegister, dataFlashAddress, cancellationToken).ConfigureAwait(false);
            await WriteSingleRegisterAsync(DfCommandArgRegister, value, cancellationToken).ConfigureAwait(false);
            await WriteSingleRegisterAsync(DfCommandArg2Register, 0, cancellationToken).ConfigureAwait(false);
            await WriteSingleRegisterAsync(DfCommandExecRegister, 3, cancellationToken).ConfigureAwait(false);
            await Task.Delay(DfCommandDelayMs, cancellationToken).ConfigureAwait(false);
        }

        public async Task WriteDataFlashRegister32Async(ushort dataFlashAddress, uint value, CancellationToken cancellationToken)
        {
            await WriteSingleRegisterAsync(DfCommandRegRegister, dataFlashAddress, cancellationToken).ConfigureAwait(false);
            await WriteSingleRegisterAsync(DfCommandArgRegister, (ushort)(value & 0xFFFF), cancellationToken).ConfigureAwait(false);
            await WriteSingleRegisterAsync(DfCommandArg2Register, (ushort)((value >> 16) & 0xFFFF), cancellationToken).ConfigureAwait(false);
            await WriteSingleRegisterAsync(DfCommandExecRegister, 3, cancellationToken).ConfigureAwait(false);
            await Task.Delay(DfCommandDelayMs, cancellationToken).ConfigureAwait(false);
        }

        public Task DisconnectAsync()
        {
            if (_serialPort != null)
            {
                try
                {
                    _serialPort.Close();
                }
                finally
                {
                    _serialPort.Dispose();
                    _serialPort = null;
                }
            }

            Log?.Invoke(this, "Disconnected");
            return Task.CompletedTask;
        }

        private byte[] Exchange(byte[] request, CancellationToken cancellationToken)
        {
            Exception lastError = null;

            for (int attempt = 0; attempt < RetryCount; attempt++)
            {
                try
                {
                    lock (_ioLock)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        _serialPort.DiscardInBuffer();
                        _serialPort.Write(request, 0, request.Length);

                        byte[] header = ReadExact(2, cancellationToken);
                        if (header[0] != _slaveId)
                        {
                            throw new InvalidOperationException("Unexpected slave id in response.");
                        }

                        byte function = header[1];
                        List<byte> response = new List<byte>(32);
                        response.AddRange(header);

                        if ((function & 0x80) != 0)
                        {
                            response.AddRange(ReadExact(3, cancellationToken));
                        }
                        else if (function == FcReadHolding)
                        {
                            byte[] byteCount = ReadExact(1, cancellationToken);
                            response.Add(byteCount[0]);
                            response.AddRange(ReadExact(byteCount[0] + 2, cancellationToken));
                        }
                        else if (function == FcWriteSingle)
                        {
                            response.AddRange(ReadExact(6, cancellationToken));
                        }
                        else
                        {
                            throw new InvalidOperationException("Unsupported function code in response: " + function);
                        }

                        ValidateCrc(response.ToArray());
                        return response.ToArray();
                    }
                }
                catch (Exception ex) when (ex is TimeoutException || ex is IOException || ex is InvalidOperationException)
                {
                    lastError = ex;
                    if (attempt < RetryCount - 1)
                    {
                        Thread.Sleep(RetryDelayMs);
                        continue;
                    }
                }
            }

            throw new InvalidOperationException("Modbus exchange failed after retries.", lastError);
        }

        private byte[] ReadExact(int length, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = _serialPort.Read(buffer, offset, length - offset);
                if (read <= 0)
                {
                    throw new TimeoutException("Modbus serial read timeout.");
                }

                offset += read;
            }

            return buffer;
        }

        private byte[] BuildReadHoldingRequest(ushort startAddress, ushort count)
        {
            byte[] request = new byte[8];
            request[0] = _slaveId;
            request[1] = FcReadHolding;
            request[2] = (byte)(startAddress >> 8);
            request[3] = (byte)(startAddress & 0xFF);
            request[4] = (byte)(count >> 8);
            request[5] = (byte)(count & 0xFF);
            AppendCrc(request, 6);
            return request;
        }

        private byte[] BuildWriteSingleRequest(ushort address, ushort value)
        {
            byte[] request = new byte[8];
            request[0] = _slaveId;
            request[1] = FcWriteSingle;
            request[2] = (byte)(address >> 8);
            request[3] = (byte)(address & 0xFF);
            request[4] = (byte)(value >> 8);
            request[5] = (byte)(value & 0xFF);
            AppendCrc(request, 6);
            return request;
        }
        private void AppendCrc(byte[] buffer, int lengthWithoutCrc)
        {
            ushort crc = ComputeCrc16(buffer, lengthWithoutCrc);
            buffer[lengthWithoutCrc] = (byte)(crc & 0xFF);
            buffer[lengthWithoutCrc + 1] = (byte)((crc >> 8) & 0xFF);
        }

        private void ValidateCrc(byte[] frame)
        {
            if (frame.Length < 4)
            {
                throw new InvalidOperationException("Response frame too short for CRC.");
            }

            ushort expected = (ushort)(frame[frame.Length - 2] | (frame[frame.Length - 1] << 8));
            ushort actual = ComputeCrc16(frame, frame.Length - 2);
            if (expected != actual)
            {
                throw new InvalidOperationException("CRC check failed.");
            }
        }

        private static ushort ComputeCrc16(byte[] data, int length)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < length; i++)
            {
                crc ^= data[i];
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }

            return crc;
        }
    }
}
