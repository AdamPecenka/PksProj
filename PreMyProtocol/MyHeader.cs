namespace PreMyProtocol;

[Flags]
public enum FlagsEnum {
	NONE =				  0,  // 0000 0000 = 0  = 0x00
	SYN =				  1,  // 0000 0001 = 1  = 0x01
	ACK =			 1 << 1,  // 0000 0010 = 2  = 0x02
	NACK =			 1 << 2,  // 0000 0100 = 4  = 0x04
	FIN =			 1 << 3,  // 0000 1000 = 8  = 0x08
	KEEP_ALIVE =	 1 << 4,  // 0001 0000 = 16 = 0x10
	TEXT_MSG_TYPE =	 1 << 5,  // 0010 0000 = 32 = 0x20
	FILE_MSG_TYPE =	 1 << 6,  // 0100 0000 = 64 = 0x40
}

public class MyHeader {
	public byte Flags { get ; set; }
	public byte FragTotal { get ; set; }
	public byte[] SeqNum { get ; set; }		// 3 bytes required
	public ushort Crc16 { get; set; }
	public List<byte> Data { get; set; }

	public MyHeader(byte flags, byte fragId, byte fragTotal, byte[] seqNum, ushort crc16, List<byte> data) {
		Flags = flags;
		FragTotal = fragTotal;
		SeqNum = seqNum;
		Crc16 = crc16;
		Data = data;
	}

	public MyHeader() {
		Flags = 0;
		FragTotal = 0;
		SeqNum = [3];
		Crc16 = 0;
		Data = new List<byte>();
	}
}