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
	static int FRAGMENT_SIZE = 1000;
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

		// lebo C# nepodporuje dva aktivne porty na loopback
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

        Dictionary<int, byte[]> fragMap = new Dictionary<int, byte[]>();
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
						Console.WriteLine("\n[!!!] Nezabudnite si nastavit cestu k priecinku na prijmanie suborov -> /help");
						Console.WriteLine("\tBy default su nastavene priecinky na pocitaci autora\n");
						break;

					case 3: // 0000 0011
						Console.WriteLine("~ [SYN,ACK]");
						Console.WriteLine("\n[?] Type /help to see commands and description\n");
                        Console.WriteLine("\n[!!!] Nezabudnite si nastavit cestu k priecinku na prijmanie suborov -> /help");
                        Console.WriteLine("\tBy default su nastavene priecinky na pocitaci autora\n");

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

						byte[] seqNum32 = new byte[4];
						byte[] fragTotal32 = new byte[4];
                        // z 3 bytov sa tazko spravi 32bit int....
                        if(recievedHeader.SeqNum.Length != 4) { 
							seqNum32 = _utils.Create32bit(recievedHeader.SeqNum);
						}
						if(recievedHeader.FragTotal.Length != 4) {
							fragTotal32 = _utils.Create32bit(recievedHeader.FragTotal);
						}

                        int seqNum = BitConverter.ToInt32(seqNum32);

                        if(seqNum == 0) {
							
                            expectedFrags = BitConverter.ToInt32(fragTotal32);
							fragMap.EnsureCapacity(expectedFrags);
							recievedFragCounter = expectedFrags;
							fileName = Encoding.ASCII.GetString(recievedHeader.Data.ToArray());
                        }
						if(seqNum != 0) {
							Console.WriteLine($"[+] {seqNum - 1}/{expectedFrags}");
							fragMap.Add(seqNum - 1, recievedHeader.Data.ToArray());

							if((seqNum - 1) == expectedFrags) {
                                var fullPath = Path.Combine(DESTINATION_FILE_PATH, fileName);
                                File.WriteAllBytes(fullPath, _utils.GetByteArrFromDict(fragMap));
                                Console.WriteLine($"\n[+] Recieved file: {fullPath}");
                                fragMap.Clear();
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
			byte[] msgBytes; 


            switch(command) {
				case "/help":
					PrintHelpMenu();
					break;

				case "/exit":
                    Console.WriteLine("[!] Under Construction");
                    return;

				case "/msg":
                    msgBytes= Encoding.ASCII.GetBytes(message);
                    
					headerToSend = new MyHeader();
					headerToSend.Flags = (byte)FlagsEnum.TEXT_MSG_TYPE;
					headerToSend.Data.AddRange(msgBytes);
					
					SendMessage(headerToSend);
					break;

				case "/dmsg":
                    msgBytes= Encoding.ASCII.GetBytes(message);

                    headerToSend = new MyHeader();
					headerToSend.Flags = (byte)FlagsEnum.TEXT_MSG_TYPE;
                    headerToSend.Data.AddRange(msgBytes);


                    SendDamagedMessage(headerToSend);
					break;

				case "/file":
					SendFile(message);		// message == file path
					break;

                case "/dfile":
                    Console.WriteLine("[!] Under Construction");
                    break;

                case "/status":
					PrintStatusMenu();
                    break;

				case "/setfsize":
					try {
                        int fragSize = int.Parse(message);

                        if(fragSize >= 1 && fragSize <= 1400) {
                            FRAGMENT_SIZE = fragSize;
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
		MyHeader initFragment;

		var fileName = Path.GetFileName(path);
		var nameBytes = Encoding.ASCII.GetBytes(fileName);
		var fileBytes = File.ReadAllBytes(path);
		var len = fileBytes.Length;

		List<byte> listFileBytes = new List<byte>(fileBytes);
        var fragments = listFileBytes.Chunk(FRAGMENT_SIZE);


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
                Console.WriteLine($"[!] {e}");
            }
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

	static void PrintHelpMenu() {
        Console.WriteLine("\n==============================================================\n");
        Console.WriteLine(@"[testDir1]			-> C:\Users\adamp\Skola\ZS2\PKS\testDir1\testFile");
        Console.WriteLine(@"[testDir1]			-> C:\Users\adamp\Skola\ZS2\PKS\testDir1\picSmall.png");
        Console.WriteLine(@"[testDir1]			-> C:\Users\adamp\Skola\ZS2\PKS\testDir1\test.txt");
        Console.WriteLine(@"[testDir2]			-> C:\Users\adamp\Skola\ZS2\PKS\testDir2");
        Console.WriteLine("/help				-> display help menu");
		Console.WriteLine("/status				-> display some info");
		Console.WriteLine("/exit				-> close the connection for both sides ");
		Console.WriteLine("/msg [string]		-> send regular text message");
		Console.WriteLine("/dmsg [string]		-> send damaged text message");
		Console.WriteLine("/file [path]		-> send file");
		Console.WriteLine("/dfile [path]		-> send damaged file");
		Console.WriteLine("/setfsize [int]		-> set fragment size (1 - 1400) ... 1400 is default size");
		Console.WriteLine("/setdir [path]		-> set local directory for recieving");
        Console.WriteLine("/clear				-> clears the console");
        Console.WriteLine("\n==============================================================\n");
    }
	static void PrintStatusMenu() {
        Console.WriteLine("\n==============================================================\n");
		Console.WriteLine($"[i] Recieving IPv4: {remoteEndPoint.Address}");
		Console.WriteLine($"[i] Local dest dir: {DESTINATION_FILE_PATH}");
        Console.WriteLine($"[i] Fragment size: {FRAGMENT_SIZE}");
        Console.WriteLine($"[i] Last Sent packet:");
        Console.WriteLine($"\t[Data] {Encoding.ASCII.GetString(lastSent.Data.ToArray())}");
        Console.WriteLine($"\t[CRC] 0x{lastSent.Crc16:X4}");
        Console.WriteLine($"\t[Flags] 0x{lastSent.Flags:X2}");
        Console.WriteLine("\n==============================================================\n");
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
}
