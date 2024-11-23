using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
#nullable disable

namespace PreMyProtocol;
public class Program {
    
	#region Globalky
    const string LOCALHOST = "127.0.0.1";
    private const float PROB = 0.1f;

    static bool ACK_RECV = false;
    static bool INIT_HAND_SHAKEN = false;
    static bool HAND_SHAKEN = false;
	static bool IS_CONNECTION = true;
	static int FRAGMENT_SIZE = 1400;
    static int MOD = 5;
	static string DESTINATION_FILE_PATH = @"C:\Users\adamp\Skola\ZS2\PKS\testDir2";
    static bool KEEP_ALIVE = false;
    static int MAX_KEEP_ALIVE_RETRIES = 3;
    static int KEEP_ALIVE_INTERVAL = 5; // sekundy

    static UdpClient sendingClient;
	static UdpClient listenerClient;
    static IPEndPoint remoteEndPoint;
	static IPEndPoint localEndPoint;
    static readonly Utils _utils = new Utils();
	static MyHeader lastSent = new();
	static Random rand = new Random();
	#endregion

    static void Main(string[] args) {

		#region Initial prompting

		Console.Write("Dest IP: ");
		string destIpAddress = Console.ReadLine();

		if(destIpAddress == "localhost") { 
			destIpAddress = LOCALHOST; 
		}

		Console.Write("Sending port: ");
		int sendingPort = int.Parse(Console.ReadLine());

		Console.Write("Listening port: ");
		int listeningPort = int.Parse(Console.ReadLine());
		
		while(sendingPort == listeningPort) {
			Console.WriteLine("[!] Duplicate port number");
			Console.Write("Enter the listening port: ");

			listeningPort = int.Parse(Console.ReadLine());
		}

		Console.WriteLine("\n... initializing ...\n");

		#endregion

		// lebo C# nepodporuje dva aktivne porty na loopback
		sendingClient = destIpAddress == LOCALHOST
			? sendingClient = new UdpClient()
			: sendingClient = new UdpClient(sendingPort);
		listenerClient = new UdpClient(listeningPort);

		remoteEndPoint = new IPEndPoint(IPAddress.Parse(destIpAddress), sendingPort);
        localEndPoint = new IPEndPoint(IPAddress.Any, listeningPort);

        Thread listenerThread = new Thread(() => StartListener());
		listenerThread.Start();

		Thread senderThread = new Thread(() => StartSender());
		senderThread.Start();

		listenerThread.Join();
		senderThread.Join();
	}

    static void StartListener() {
        MyHeader headerToSend;
        List<(int, byte[])> recvBuff = new List<(int, byte[])>();
        Stopwatch stopwatch = new Stopwatch();
        int recievedFragCounter = 0;
        int expectedFrags = 0;
        string fileName = null;

        int keepAliveRetries = 0;
        DateTime lastKeepAliveSent = DateTime.Now;

        try {
            while(IS_CONNECTION) {
                byte[] receivedBytes = listenerClient.Receive(ref localEndPoint);
                MyHeader recievedHeader = new MyHeader();
                recievedHeader = _utils.GetHeader(receivedBytes);

                switch(recievedHeader.Flags) {
                    case 1: // 0000 0001
                        INIT_HAND_SHAKEN = true;
                        Console.WriteLine("~ [SYN]");
                        headerToSend = new MyHeader();
                        headerToSend.Flags = (byte)(FlagsEnum.SYN | FlagsEnum.ACK);
                        SendMessage(headerToSend);
                        break;

                    case 2: // 0000 0010
                        Console.WriteLine("~ [ACK]");
                        ACK_RECV = true;
                        break;

                    case 3: // 0000 0011
                        Console.WriteLine("~ [SYN,ACK]");
                        headerToSend = new MyHeader();
                        headerToSend.Flags = (byte)FlagsEnum.ACK;
                        SendMessage(headerToSend);
                        KEEP_ALIVE = true;
                        lastKeepAliveSent = DateTime.Now;
                        break;

                    case 4:
                        Console.WriteLine("~ [NACK]");
                        ResendFragment(lastSent);
                        break;

                    case 16: // 0001 0000 - KEEP_ALIVE
                        if(KEEP_ALIVE == false) {
                            KEEP_ALIVE = true;
                        }

                        keepAliveRetries = 0;
                        lastKeepAliveSent = DateTime.Now;
                        Console.WriteLine("~ [KA]");
                        headerToSend = new MyHeader();
                        headerToSend.Flags = (byte)(FlagsEnum.KEEP_ALIVE | FlagsEnum.ACK);
                        SendMessage(headerToSend);
                        break;

                    case 32:
                        HandleRecievedMessage(recievedHeader);
                        break;

                    case 64: // 0100 0000 - File
                        byte[] seqNum32 = new byte[4];
                        byte[] fragTotal32 = new byte[4];

                        seqNum32 = _utils.Create32bit(recievedHeader.SeqNum);
                        fragTotal32 = _utils.Create32bit(recievedHeader.FragTotal);

                        int seqNum = BitConverter.ToInt32(seqNum32);

                        #region CRC Check
                        ushort expectedCrc16 = _utils.GetCrc16(recievedHeader.Data.ToArray());
                        if(expectedCrc16 != recievedHeader.Crc16) {
                            MyHeader reply = new MyHeader();
                            reply.Flags = (byte)FlagsEnum.NACK;
                            SendMessage(reply);

                            Console.WriteLine($"[-] Recieved damaged fragment (Seq: {seqNum - 1}), sending again...");
                            continue;
                        }
                        else if(expectedCrc16 == recievedHeader.Crc16 && ACK_RECV != true) {
                            MyHeader reply = new MyHeader();
                            reply.Flags = (byte)FlagsEnum.ACK;
                            SendMessage(reply);
                        }
                        #endregion

                        if(seqNum == 0) {
                            stopwatch.Start();
                            expectedFrags = BitConverter.ToInt32(fragTotal32);
                            recvBuff.EnsureCapacity(expectedFrags);
                            recievedFragCounter = expectedFrags;
                            fileName = Encoding.ASCII.GetString(recievedHeader.Data.ToArray());
                        }
                        if(seqNum != 0) {
                            Console.WriteLine($"[+] {seqNum - 1}/{expectedFrags}");
                            recvBuff.Add((seqNum - 1, recievedHeader.Data.ToArray()));

                            // If all fragments received
                            if((seqNum - 1) == expectedFrags) {
                                // Sort the fragments by their sequence number before concatenating
                                var sortedList = recvBuff.OrderBy(x => x.Item1).ToList();

                                // Concatenate the sorted data into a single byte array
                                List<byte> fullFileData = new List<byte>();
                                foreach(var (seq, data) in sortedList) {
                                    fullFileData.AddRange(data);
                                }

                                var fullPath = Path.Combine(DESTINATION_FILE_PATH, fileName);
                                File.WriteAllBytes(fullPath, fullFileData.ToArray());

                                stopwatch.Stop();
                                TimeSpan ts = stopwatch.Elapsed;
                                string elapsedTime =
                                    String.Format("{0:00}:{1:00}.{2:00}", ts.Minutes, ts.Seconds, ts.Milliseconds / 10);

                                Console.WriteLine($"\n[t] {elapsedTime}");
                                Console.WriteLine($"[+] Recieved file: {fullPath}");

                                recvBuff.Clear();
                                stopwatch.Reset();
                            }
                        }
                        break;

                    default:
                        Console.WriteLine("[!!!] Picovinu som dostal, pomoc");
                        break;
                }
            }
        }
        catch(Exception e) {
            Console.WriteLine(e.ToString());
        }
    }


    static void StartSender() {
        MyHeader headerToSend;

        Thread.Sleep(5000);
        if(!INIT_HAND_SHAKEN) {
            headerToSend = new MyHeader();
            headerToSend.Flags = (byte)FlagsEnum.SYN;
            SendMessage(headerToSend);
        }

        while(IS_CONNECTION) {

            string input = Console.ReadLine().Trim();
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            string command = parts.Length > 0
                ? parts[0]
                : "";
            string message = parts.Length > 1
                ? string.Join(" ", parts, 1, parts.Length - 1)
                : "";
            byte[] msgBytes;

            switch(command) {
                case "/help":
                    _utils.PrintHelpMenu();
                    break;

                case "/msg":
                    msgBytes = Encoding.ASCII.GetBytes(message);

                    headerToSend = new MyHeader();
                    headerToSend.Flags = (byte)FlagsEnum.TEXT_MSG_TYPE;
                    headerToSend.Data.AddRange(msgBytes);

                    SendMessage(headerToSend);
                    break;

                case "/dmsg":
                    msgBytes = Encoding.ASCII.GetBytes(message);

                    headerToSend = new MyHeader();
                    headerToSend.Flags = (byte)FlagsEnum.TEXT_MSG_TYPE;
                    headerToSend.Data.AddRange(msgBytes);

                    SendDamagedMessage(headerToSend);
                    break;

                case "/file":
                    SendFile(message);        // message == file path
                    break;

                case "/dfile":
                    SendDamagedFile(message);
                    break;

                case "/status":
                    PrintStatusMenu();
                    break;

                case "/setfsize":
                    try {
                        int fragSize = int.Parse(message);

                        if(fragSize >= 1 && fragSize <= 1400) {
                            FRAGMENT_SIZE = fragSize;
                            Console.WriteLine($"[+] Fragment size set to: {FRAGMENT_SIZE}");
                        }
                        else {
                            Console.WriteLine("[!] Incorrect fragment size");
                        }
                    }
                    catch(Exception e) {
                        Console.WriteLine($"[!] {e}");
                    }
                    break;

                case "/setdir":
                    DESTINATION_FILE_PATH = message;
                    Console.WriteLine($"[+] Destination set to: {DESTINATION_FILE_PATH}");
                    break;

                case "/setmodulo":
                    int newMod = int.Parse(message);
                    MOD = newMod;
                    Console.WriteLine($"[+] Destination set to: {DESTINATION_FILE_PATH}");
                    break;

                case "/clear":
                    Console.Clear();
                    break;

                case "\n":
                    break;

                default:
                    Console.WriteLine("[!] Invalid command, use /help if you don't know what to do ;)");
                    break;
            }
        }
    }


    static void SendMessage(MyHeader message) {

		if(message.Data != null) {
			message.Crc16 = _utils.GetCrc16(message.Data.ToArray());
		}

		lastSent = new MyHeader() {
			Flags = message.Flags,
			FragTotal = message.FragTotal,
			SeqNum = message.SeqNum,
			Crc16 = message.Crc16,
			Data = message.Data
		};

		var sendBytes = _utils.GetByteArr(message);
		SendWhatever(sendBytes);
	}
    static void ResendFragment(MyHeader message) {
        byte[] seqNum32 = new byte[4];
        seqNum32 = _utils.Create32bit(message.SeqNum);

        int seqNum = BitConverter.ToInt32(seqNum32);

        Console.WriteLine($"[<<] SeqNum: {seqNum - 1}");

        if(message.Data != null) {
            message.Crc16 = _utils.GetCrc16(message.Data.ToArray());
        }

        lastSent = new MyHeader() {
            Flags = message.Flags,
            FragTotal = message.FragTotal,
            SeqNum = message.SeqNum,
            Crc16 = message.Crc16,
            Data = message.Data
        };

        var sendBytes = _utils.GetByteArr(message);
        SendWhatever(sendBytes);
    }
    static void SendDamagedMessage(MyHeader message) {

        if(message.Data != null) {
            message.Crc16 = _utils.GetCrc16(message.Data.ToArray());
        }

        lastSent = new MyHeader() {
            Flags = message.Flags,
            FragTotal = message.FragTotal,
            SeqNum = message.SeqNum,
            Crc16 = message.Crc16,
            Data = message.Data
        };


        List<byte> damagedData = new List<byte>(message.Data);
		_utils.DamageRandomFragments(damagedData);
        message.Data = damagedData;

        var sendBytes = _utils.GetByteArr(message);
		SendWhatever(sendBytes);
    }
	static void SendFile(string path) {
		MyHeader initFragment;

		var fileName = Path.GetFileName(path);
		var nameBytes = Encoding.ASCII.GetBytes(fileName);
		
		var fileBytes = File.ReadAllBytes(path);
		List<byte> listFileBytes = new List<byte>(fileBytes);
        var fragments = listFileBytes.Chunk(FRAGMENT_SIZE).ToList();

		var len = fileBytes.Length;

        initFragment = new MyHeader() {
			Flags = (byte)FlagsEnum.FILE_MSG_TYPE,
			FragTotal = BitConverter.GetBytes(fragments.Count() - 1),
			SeqNum = BitConverter.GetBytes(0),
			Crc16 = _utils.GetCrc16(nameBytes),
			Data = new List<byte>(nameBytes)
        };

        lastSent = new MyHeader() {
            Flags = initFragment.Flags,
            FragTotal = initFragment.FragTotal,
            SeqNum = initFragment.SeqNum,
            Crc16 = initFragment.Crc16,
            Data = initFragment.Data
        };

        var sendInitBytes = _utils.GetByteArr(initFragment);
		SendWhatever(sendInitBytes);

		int idx = 0;
		foreach(var frag in fragments) {
            Console.WriteLine($"[+] SeqNum: {idx}; Size: {frag.Length}");
			try {
                MyHeader fragment = new MyHeader() {
                    Flags = (byte)FlagsEnum.FILE_MSG_TYPE,
                    FragTotal = BitConverter.GetBytes(0),
                    SeqNum = BitConverter.GetBytes(idx + 1),
                    Crc16 = _utils.GetCrc16(frag),
                    Data = new List<byte>(frag)
                };

                lastSent = new MyHeader() {
                    Flags = fragment.Flags,
                    FragTotal = fragment.FragTotal,
                    SeqNum = fragment.SeqNum,
                    Crc16 = fragment.Crc16,
                    Data = fragment.Data
                };

                var sendFragBytes = _utils.GetByteArr(fragment);
                SendWhatever(sendFragBytes);

			}
            catch(Exception e) {
                Console.WriteLine("[!] Failed to send fragment" + e);
            }

            //while(!ACK_RECV) {
            //    Thread.Yield();
            //}
            //ACK_RECV = false;

            idx++;
        }
    }
    static void SendDamagedFile(string path) {
        MyHeader initFragment;

        var fileName = Path.GetFileName(path);
        var nameBytes = Encoding.ASCII.GetBytes(fileName);

        var fileBytes = File.ReadAllBytes(path);
        List<byte> listFileBytes = new List<byte>(fileBytes);
		var fragments = listFileBytes.Chunk(FRAGMENT_SIZE).ToList();

        var len = fileBytes.Length;

        initFragment = new MyHeader() {
            Flags = (byte)FlagsEnum.FILE_MSG_TYPE,
            FragTotal = BitConverter.GetBytes(fragments.Count() - 1),
            SeqNum = BitConverter.GetBytes(0),
            Crc16 = _utils.GetCrc16(nameBytes),
            Data = new List<byte>(nameBytes)
        };

        lastSent = new MyHeader() {
            Flags = initFragment.Flags,
            FragTotal = initFragment.FragTotal,
            SeqNum = initFragment.SeqNum,
            Crc16 = initFragment.Crc16,
            Data = initFragment.Data
        };

        var sendInitBytes = _utils.GetByteArr(initFragment);
		SendWhatever(sendInitBytes);
		
		int idx = 0;
        foreach(var frag in fragments) {
            Console.WriteLine($"[<] SeqNum: {idx}; Size: {frag.Length}");
            try {
                MyHeader fragment = new MyHeader() {
                    Flags = (byte)FlagsEnum.FILE_MSG_TYPE,
                    FragTotal = BitConverter.GetBytes(0),
                    SeqNum = BitConverter.GetBytes(idx + 1),
                    Crc16 = _utils.GetCrc16(frag),
                    Data = new List<byte>(frag)
                };

                lastSent = new MyHeader() {
                    Flags = fragment.Flags,
                    FragTotal = fragment.FragTotal,
                    SeqNum = fragment.SeqNum,
                    Crc16 = fragment.Crc16,
                    Data = fragment.Data
                };

                if(idx % MOD == 1) {
                    List<byte> damagedData;
                    damagedData = new List<byte>(fragment.Data);
                    _utils.DamageRandomFragments(damagedData);
                    fragment.Data = damagedData;
                    
                }
				
                var sendFragBytes = _utils.GetByteArr(fragment);
                SendWhatever(sendFragBytes);
            }
            catch(Exception e) {
                Console.WriteLine("[!] Failed to send fragment" + e);
            }

            while(!ACK_RECV) {
                Thread.Sleep(1);
                //Thread.Yield();

            }
            ACK_RECV = false;

            idx++;
        }
    }

    static void HandleRecievedMessage(MyHeader packet) {
		ushort expectedCrc16 = _utils.GetCrc16(packet.Data.ToArray());
		
		if(expectedCrc16 != packet.Crc16) {
			MyHeader reply = new();
			reply.Flags = (byte)FlagsEnum.NACK;

            SendMessage(reply);

            Console.WriteLine("[-] Recieved damaged packet, sending again...");
		}
		else {
			string msg = Encoding.ASCII.GetString(packet.Data.ToArray());
			Console.WriteLine($">> {msg}");
		}
	}

	static void SendWhatever(byte[] bytes) {
        try {
            sendingClient.Send(bytes, bytes.Length, remoteEndPoint);
        }
        catch(SocketException se) {
            Console.WriteLine("[!] Failed to send packet");
            Console.WriteLine(se.ToString());
        }
    }
    static void PrintStatusMenu() {
        byte[] seqNum32 = new byte[4];
        byte[] fragTotal32 = new byte[4];

        seqNum32 = _utils.Create32bit(lastSent.SeqNum);
        fragTotal32 = _utils.Create32bit(lastSent.FragTotal);

        int seqNum = BitConverter.ToInt32(seqNum32);
        int fragTotal = BitConverter.ToInt32(fragTotal32);

        Console.WriteLine("\n==============================================================\n");
        Console.WriteLine($"[i] Recieving IPv4: {remoteEndPoint.Address}");
        Console.WriteLine($"[i] Local dest dir: {DESTINATION_FILE_PATH}");
        Console.WriteLine($"[i] Fragment size: {FRAGMENT_SIZE}");
        Console.WriteLine($"[i] Fragment modulo: {MOD}");
        Console.WriteLine($"[i] Last Sent packet:");
        Console.WriteLine($"\t[Flags] 0x{lastSent.Flags:X2}");
        Console.WriteLine($"\t[FragTotal] {fragTotal}");
        Console.WriteLine($"\t[SeqNum] {seqNum}");
        Console.WriteLine($"\t[CRC] 0x{lastSent.Crc16:X4}");
        Console.WriteLine($"\t[Data] {Encoding.ASCII.GetString(lastSent.Data.ToArray())}");
        Console.WriteLine("\n==============================================================\n");
    }
}
