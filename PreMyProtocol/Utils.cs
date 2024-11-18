using System.Text;

namespace PreMyProtocol;

public class Utils {

	private const int HEADER_SIZE = 6;
	private const ushort STD_POLYNOM = 0x1021;
	private const ushort MSB = 0x8000;

	public MyHeader GetHeader(byte[] packet) {

		MyHeader decodedHeader = new MyHeader();

		decodedHeader.Flags = packet[0];
		decodedHeader.FragId = packet[1];
		decodedHeader.FragTotal = packet[2];
		decodedHeader.SeqNum = packet[3];
		decodedHeader.Crc16 = BitConverter.ToUInt16(packet, 4);
		decodedHeader.Data = new byte[packet.Length - 6];
		Array.Copy(packet, 6, decodedHeader.Data, 0, decodedHeader.Data.Length);

		return decodedHeader;
	}

	public byte[] GetByteArr(MyHeader msg) {

		int totalLength = HEADER_SIZE 
			+ (msg.Data != null 
				? msg.Data.Length 
				: 0);
		
		
		byte[] byteArray = new byte[totalLength];

		byteArray[0] = msg.Flags;
		byteArray[1] = msg.FragId;
		byteArray[2] = msg.FragTotal;
		byteArray[3] = msg.SeqNum;
		BitConverter.GetBytes(msg.Crc16).CopyTo(byteArray, 4);

		if(msg.Data != null) {
			Array.Copy(msg.Data, 0, byteArray, 6, msg.Data.Length);
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
