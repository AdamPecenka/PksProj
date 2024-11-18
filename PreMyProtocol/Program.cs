using System.Net;
using System.Net.Sockets;
using System.Text;
#nullable disable

namespace PreMyProtocol;
public class Program {
    
	#region Globalky
    const string LOCALHOST = "127.0.0.1";

	static bool HAND_SHAKEN = false;
	static bool IS_CONNECTION = true;
	static int FRAGMENT_SIZE = 1400;
	static string DESTINATION_FILE_PATH = @"C:\Users\adamp\Skola\ZS2\PKS\testDir2";

	static UdpClient sendingClient;         // sending port
	static IPEndPoint remoteEndPoint;
	static readonly Utils _utils = new Utils();
	static MyHeader lastSent = new();
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

		// lebo C# nepodporuje dva porty na loopback
		sendingClient = destIpAddress == LOCALHOST
			? sendingClient = new UdpClient()
			: sendingClient = new UdpClient(sendingPort);

		remoteEndPoint = new IPEndPoint(IPAddress.Parse(destIpAddress), sendingPort);

		Thread listenerThread = new Thread(() => StartListener(listeningPort));
		listenerThread.Start();

		Thread senderThread = new Thread(() => StartSender());
		senderThread.Start();

		listenerThread.Join();
		senderThread.Join();
	}

	static void StartListener(int listeningPort) {
		
		using UdpClient listenerClient = new UdpClient(listeningPort);
		IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, listeningPort);
		MyHeader headerToSend;
		byte[] toBeFile = null;
		int recievedFragCounter = 0;
		int expectedFrags = 0;
		string fileName = null;

		try {
			while(IS_CONNECTION) {
				
				byte[] receivedBytes = listenerClient.Receive(ref localEndPoint);
				MyHeader recievedHeader = new MyHeader();
				recievedHeader = _utils.GetHeader(receivedBytes);

				switch(recievedHeader.Flags) {
					case 1: // 0000 0001
						HAND_SHAKEN = true;
						Console.WriteLine("~ [SYN]");

						headerToSend = new MyHeader();
						headerToSend.Flags = (byte)(FlagsEnum.SYN | FlagsEnum.ACK);
						SendMessage(headerToSend);
						break;

					case 2: // 0000 0010
						Console.WriteLine("~ [ACK]");
						Console.WriteLine("\n[?] Type /help to see commands and description\n");
						break;

					case 3: // 0000 0011
						Console.WriteLine("~ [SYN,ACK]");
						Console.WriteLine("\n[?] Type /help to see commands and description\n");

						headerToSend = new MyHeader();
						headerToSend.Flags = (byte)FlagsEnum.ACK;
						SendMessage(headerToSend);
						break;

					case 4: //0000 0100
						Console.WriteLine("~ [NACK]");
						SendMessage(lastSent);
						break;

					case 32: // 0010 0000 - Text
						HandleRecievedMessage(recievedHeader);
						break;

					case 64: // 0100 0000 - File

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
		if(!HAND_SHAKEN) {
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
			byte[] msgBytes = Encoding.ASCII.GetBytes(message);


            switch(command) {
				case "/help":
					PrintHelpMenu();
					break;

				case "/exit":
                    Console.WriteLine("[!] Under Construction");
                    return;

				case "/msg":
                    headerToSend = new MyHeader();
					headerToSend.Flags = (byte)FlagsEnum.TEXT_MSG_TYPE;
					headerToSend.Data.AddRange(msgBytes);
					
					SendMessage(headerToSend);
					break;

				case "/dmsg":
                    headerToSend = new MyHeader();
					headerToSend.Flags = (byte)FlagsEnum.TEXT_MSG_TYPE;
                    headerToSend.Data.AddRange(msgBytes);


                    SendDamagedMessage(headerToSend);
					break;

				case "/file":
					SendFile(message);
					break;

				case "/status":
					PrintStatusMenu();
                    break;

				case "/fragsize":
					try {
                        int fragsize = int.Parse(message);

                        if(fragsize >= 1 && fragsize <= 1400) {
                            FRAGMENT_SIZE = fragsize;
						}
						else {
                            Console.WriteLine("[!] Incorrect fragment size");
                        }
					}
					catch(Exception e) {
						Console.WriteLine($"[!] {e}");
					}
					break;

				case "/clear":
					Console.Clear();
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
        if(damagedData.Count > 0) {
            damagedData[0] ^= 0x01;     // flip the last bit to mismatch the CRC
        }
        message.Data = damagedData;

        var sendBytes = _utils.GetByteArr(message);
		SendWhatever(sendBytes);
    }
	static void SendFile(string path) {
		MyHeader headerToSend;

		var fileName = Path.GetFileName(path);
		var nameBytes = Encoding.ASCII.GetBytes(fileName);
		var fileBytes = File.ReadAllBytes(path);
		var len = fileBytes.Length;

		headerToSend = new MyHeader() {
			Flags = (byte)FlagsEnum.FILE_MSG_TYPE,
			FragTotal = BitConverter.GetBytes(len),
			SeqNum = new byte[3],
			Crc16 = _utils.GetCrc16(nameBytes),
			Data = new List<byte>(nameBytes)
        };

        lastSent = new MyHeader() {
            Flags = headerToSend.Flags,
            FragTotal = headerToSend.FragTotal,
            SeqNum = headerToSend.SeqNum,
            Crc16 = headerToSend.Crc16,
            Data = headerToSend.Data
        };

        var sendNameBytes = _utils.GetByteArr(headerToSend);
		SendWhatever(sendNameBytes);
		
		try {
            for(int i = 0; i < len; i++) {

                byte[] singleByte = [fileBytes[i]];

				headerToSend = new MyHeader() {
					SeqNum = BitConverter.GetBytes(i + 1)
				};

                var sendFragBytes = _utils.GetByteArr(headerToSend);
                SendWhatever(sendFragBytes);
            }
        }
        catch(Exception e) {
            Console.WriteLine($"[!] {e}");
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

	static void PrintHelpMenu() {
        Console.WriteLine("\n==============================================================\n");
        Console.WriteLine(@"[testDir1]      -> C:\Users\adamp\Skola\ZS2\PKS\testDir1\testFile");
        Console.WriteLine(@"[testDir2]      -> C:\Users\adamp\Skola\ZS2\PKS\testDir2\testFile");
        Console.WriteLine("/help            -> display help menu");
		Console.WriteLine("/exit            -> close the connection for both sides ");
		Console.WriteLine("/msg [string]    -> send regular text message");
		Console.WriteLine("/dmsg [string]   -> send damaged text message");
		Console.WriteLine("/file [path]     -> send file");
		Console.WriteLine("/dfile [path]    -> send damaged file");
		Console.WriteLine("/status			  -> display some info");
		Console.WriteLine("/fragsize [int]	  -> set fragment size (1 - 1400) ... 1400 is default size");
        Console.WriteLine("/clear           -> clears the console");
        Console.WriteLine("\n==============================================================\n");
    }
	static void PrintStatusMenu() {
        Console.WriteLine("\n==============================================================\n");
		Console.WriteLine($"[i] Destination IPv4: {remoteEndPoint.Address}");
        Console.WriteLine($"[i] Fragment size: {FRAGMENT_SIZE}");
        Console.WriteLine($"[i] Last Sent packet:");
        Console.WriteLine($"\t[Data] {Encoding.ASCII.GetString(lastSent.Data.ToArray())}");
        Console.WriteLine($"\t[CRC] 0x{lastSent.Crc16:X4}");
        Console.WriteLine($"\t[Flags] 0x{lastSent.Flags:X2}");
        Console.WriteLine("\n==============================================================\n");
    }
	public static void SendWhatever(byte[] bytes) {
        try {
            sendingClient.Send(bytes, bytes.Length, remoteEndPoint);
        }
        catch(SocketException se) {
            Console.WriteLine("[!] Failed to send packet");
            Console.WriteLine(se.ToString());
        }
    }
}
