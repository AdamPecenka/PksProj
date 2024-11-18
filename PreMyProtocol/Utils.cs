using System.Text;

namespace PreMyProtocol;

public class Utils {

	private const int HEADER_SIZE = 8;
	private const ushort STD_POLYNOM = 0x1021;
	private const ushort MSB = 0x8000;

	public MyHeader GetHeader(byte[] packet) {

        MyHeader decodedHeader = new MyHeader();

        decodedHeader.Flags = packet[0];
        decodedHeader.FragTotal = packet[2];

        // Copy the SeqNum which is 3 bytes
        decodedHeader.SeqNum = new byte[3];
        Array.Copy(packet, 3, decodedHeader.SeqNum, 0, 3);

        decodedHeader.Crc16 = BitConverter.ToUInt16(packet, 6);

        // Copy the remaining bytes to Data
        int dataLength = packet.Length - HEADER_SIZE;
        decodedHeader.Data = new List<byte>(dataLength);
        for(int i = HEADER_SIZE; i < packet.Length; i++) {
            decodedHeader.Data.Add(packet[i]);
        }

		return decodedHeader;
    }

	public byte[] GetByteArr(MyHeader msg) {

		int totalLength = HEADER_SIZE 
			+ (msg.Data != null 
				? msg.Data.Count 
				: 0);

        byte[] byteArray = new byte[totalLength];

        byteArray[0] = msg.Flags;
        byteArray[2] = msg.FragTotal;

        // Check if SeqNum is initialized and copy it
        if(msg.SeqNum != null && msg.SeqNum.Length >= 3) {
            Array.Copy(msg.SeqNum, 0, byteArray, 3, 3);
        }
        else {
            // Initialize SeqNum with zeros if it is null or too short
            for(int i = 3; i < 6; i++) {
                byteArray[i] = 0;
            }
        }

        BitConverter.GetBytes(msg.Crc16).CopyTo(byteArray, 6);

        if(msg.Data != null) {
            msg.Data.CopyTo(byteArray, HEADER_SIZE);
        }

        return byteArray;
    }

	public ushort GetCrc16(byte[] data) {
		ushort crc = 0;

		foreach(byte b in data) {
			crc ^= (ushort)(b << 8);

			for(int i = 0; i < 8; i++) {
				crc = (ushort)(((crc & 0x8000) != 0)
					? ((crc << 1) ^ STD_POLYNOM)
					: (crc << 1));
			}
		}

		return crc;
	}
}
