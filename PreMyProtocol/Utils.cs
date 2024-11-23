namespace PreMyProtocol;

public class Utils {

	private const int HEADER_SIZE = 9;
	private const ushort STD_POLYNOM = 0x1021;
    private static Random rand = new Random();
	private const ushort MSB = 0x8000;
    private const float PROB = 0.33f;

    public MyHeader GetHeader(byte[] packet) {
        MyHeader decodedHeader = new MyHeader {
            Flags = packet[0],
            FragTotal = [ packet[1], packet[2], packet[3] ],
            SeqNum = [packet[4], packet[5], packet[6]],
            Crc16 = BitConverter.ToUInt16(packet, 7),
            Data = new List<byte>(packet.Skip(HEADER_SIZE).ToArray())
        };

        return decodedHeader;
    }

	public byte[] GetByteArr(MyHeader msg) {

		int totalLength = HEADER_SIZE 
			+ (msg.Data != null 
				? msg.Data.Count 
				: 0);

        byte[] byteArray = new byte[totalLength];

        byteArray[0] = msg.Flags;

        if(msg.FragTotal != null && msg.FragTotal.Length > 0) {
            Array.Copy(msg.FragTotal, 0, byteArray, 1, 3);
        }
        else {
            for(int i = 1; i <= 3; i++) {
                byteArray[i] = 0;
            }
        }


        if(msg.SeqNum != null && msg.SeqNum.Length > 0) {
            Array.Copy(msg.SeqNum, 0, byteArray, 4, 3);
        }
        else {
            for(int i = 4; i <= 6; i++) {
                byteArray[i] = 0;
            }
        }

        BitConverter.GetBytes(msg.Crc16).CopyTo(byteArray, 7);

        if(msg.Data != null) {
            msg.Data.CopyTo(byteArray, HEADER_SIZE);
        }

        return byteArray;
    }

    public byte[] GetByteArrFromDict(Dictionary<int, byte[]> dictionary) {
        
        int totalLength = 0;
        foreach(KeyValuePair<int, byte[]> kvp in dictionary) {
            totalLength += kvp.Value.Length;
        }

        byte[] result = new byte[totalLength];
        int currentIndex = 0;

        foreach(var kvp in dictionary) {
            byte[] value = kvp.Value;
            Array.Copy(value, 0, result, currentIndex, value.Length);
            currentIndex += value.Length;
        }

        return result;
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

    // Lebo BitConverter potrebuje 4 byty pre integer...
    public byte[] Create32bit(byte[] bytes) {
        byte[] res = new byte[4];
        Array.Copy(bytes, 0, res, 0, bytes.Length);
        return res;
    }

    public void DamageRandomFragments(List<byte> data) {

        for(int i = 0; i < data.Count; i++) {
            if(rand.NextSingle() < PROB){
                data[i] ^= 0x01; 
            }
        }
    }

    public void PrintHelpMenu() {
        Console.WriteLine("\n==============================================================\n");
        Console.WriteLine(@"C:\Users\adamp\Skola\ZS2\PKS\testDir1\testFile");
        Console.WriteLine(@"C:\Users\adamp\Skola\ZS2\PKS\testDir1\picSmall.png");
        Console.WriteLine(@"C:\Users\adamp\Skola\ZS2\PKS\testDir1\stressTest.pptx");
        Console.WriteLine(@"C:\Users\adamp\Skola\ZS2\PKS\testDir1\test.txt");
        Console.WriteLine(@"C:\Users\adamp\Skola\ZS2\PKS\testDir1\2MB.txt");
        Console.WriteLine(@"C:\Users\adamp\Skola\ZS2\PKS\testDir1\sub1400.txt");
        Console.WriteLine(@"C:\Users\adamp\Skola\ZS2\PKS\testDir2");
        Console.WriteLine("/help				-> display help menu");
        Console.WriteLine("/status				-> display some info");
        Console.WriteLine("/msg [string]		-> send regular text message");
        Console.WriteLine("/dmsg [string]		-> send damaged text message");
        Console.WriteLine("/file [path]		-> send file");
        Console.WriteLine("/dfile [path]		-> send damaged file");
        Console.WriteLine("/setfsize [int]		-> set fragment size (1 - 1400) ... 1400 is default size");
        Console.WriteLine("/setdir [path]		-> set local directory for recieving");
        Console.WriteLine("/setmodulo [int]	-> set how often will fragments get damaged (default: 5)");
        Console.WriteLine("/clear				-> clears the console");
        Console.WriteLine("\n==============================================================\n");
    }
}
