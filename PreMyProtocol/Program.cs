using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Timers;
#nullable disable

namespace PreMyProtocol;
public class Program {

    #region Globalky
    const string LOCALHOST = "127.0.0.1";
    const int MAX_KEEP_ALIVE_RETRIES = 3;
    const int KEEP_ALIVE_INTERVAL = 5000; // milisekudny

    static bool ACK_RECV = false;
    static bool INIT_HAND_SHAKEN = false;
    static bool HAND_SHAKE_DONE = false;
    static bool IS_CONNECTION = true;
    static int FRAGMENT_SIZE = 1400;
    static int MOD = 5;
    static int KEEP_ALIVE_IDX = 0;
    static string DESTINATION_FILE_PATH = @"C:\Users\adamp\Skola\ZS2\PKS\testDir2";

    static UdpClient sendingClient;
    static UdpClient listenerClient;
    static IPEndPoint remoteEndPoint;
    static IPEndPoint localEndPoint;
    static readonly Utils _utils = new Utils();
    static MyHeader lastSent = new();
    static Random rand = new Random();
    static System.Timers.Timer kaTimer;
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


        kaTimer = new(KEEP_ALIVE_INTERVAL);


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
        int expectedFrags = 0;
        string fileName = string.Empty;

        try {
            while(IS_CONNECTION) {

                byte[] receivedBytes = listenerClient.Receive(ref localEndPoint);
                MyHeader recievedHeader = new MyHeader();
                recievedHeader = _utils.GetHeader(receivedBytes);

                byte[] seqNum32;
                byte[] fragTotal32;

                int seqNum;
                ushort expectedCrc16;

                switch(recievedHeader.Flags) {
                    // 0000 0001 - SYN
                    case 1:
                        INIT_HAND_SHAKEN = true;
                        Console.WriteLine("~ [SYN]");
                        headerToSend = new MyHeader();
                        headerToSend.Flags = (byte)(FlagsEnum.SYN | FlagsEnum.ACK);
                        SendFlags(headerToSend);
                        break;

                    // 0000 0010 - ACK
                    case 2:
                        Console.WriteLine("~ [ACK]");

                        if(!HAND_SHAKE_DONE) {
                            Console.WriteLine("\n[?] Type /help to see commands and description\n");
                            Console.WriteLine("\n[!!!] Nezabudnite si nastavit cestu k priecinku na prijmanie suborov -> /help");
                            Console.WriteLine("\tBy default su nastavene priecinky na pocitaci autora\n");

                            HAND_SHAKE_DONE = true;

                            kaTimer.Elapsed += OnTimedEvent;
                            kaTimer.AutoReset = true;
                            kaTimer.Enabled = true;

                            continue;
                        }

                        if(HAND_SHAKE_DONE) {
                            ACK_RECV = true;
                            ResetKATimer();
                        }
                        break;

                    // 0000 0011 - SYN, ACK
                    case 3:
                        Console.WriteLine("~ [SYN,ACK]");

                        headerToSend = new MyHeader();
                        headerToSend.Flags = (byte)FlagsEnum.ACK;
                        SendFlags(headerToSend);

                        if(!HAND_SHAKE_DONE) {
                            Console.WriteLine("\n[?] Type /help to see commands and description\n");
                            Console.WriteLine("\n[!!!] Nezabudnite si nastavit cestu k priecinku na prijmanie suborov -> /help");
                            Console.WriteLine("\tBy default su nastavene priecinky na pocitaci autora\n");

                            HAND_SHAKE_DONE = true;

                            // aby sa nestalo ze naraz posielaju KA
                            Thread.Sleep(250);
                            kaTimer.Elapsed += OnTimedEvent;
                            kaTimer.AutoReset = true;
                            kaTimer.Enabled = true;
                        }
                        break;

                    // 0000 0100 - NACK
                    case 4:
                        Console.WriteLine("~ [NACK]");
                        ResendFragment(lastSent);
                        ResetKATimer();
                        break;

                    // 0001 0000 - KEEP_ALIVE
                    case 16:
                        //Console.WriteLine("~ [KEEP_ALIVE]");

                        headerToSend = new MyHeader() {
                            Flags = (byte)(FlagsEnum.KEEP_ALIVE | FlagsEnum.ACK)
                        };
                        SendFlags(headerToSend);
                        ResetKATimer();
                        break;

                    // 0001 0000 - KEEP_ALIVE, ACK
                    case 18:
                        //Console.WriteLine("~ [KEEP_ALIVE, ACK]");
                        KEEP_ALIVE_IDX = 0;

                        break;

                    // 0010 0000 - TXT_MSG_TYPE
                    case 32:
                        seqNum32 = new byte[4];
                        fragTotal32 = new byte[4];

                        seqNum32 = _utils.Create32bit(recievedHeader.SeqNum);
                        fragTotal32 = _utils.Create32bit(recievedHeader.FragTotal);

                        seqNum = BitConverter.ToInt32(seqNum32);

                        #region CRC Check
                        expectedCrc16 = _utils.GetCrc16(recievedHeader.Data.ToArray());
                        if(expectedCrc16 != recievedHeader.Crc16) {
                            MyHeader reply = new MyHeader();
                            reply.Flags = (byte)FlagsEnum.NACK;
                            SendFlags(reply);

                            Console.WriteLine($"[-] Recieved damaged fragment (Seq: {seqNum}), sending again...");
                            continue;
                        }
                        else if(expectedCrc16 == recievedHeader.Crc16 && ACK_RECV != true) {
                            MyHeader reply = new MyHeader();
                            reply.Flags = (byte)FlagsEnum.ACK;
                            SendFlags(reply);
                        }
                        #endregion

                        if(seqNum == 0) {
                            expectedFrags = BitConverter.ToInt32(fragTotal32);
                            recvBuff.EnsureCapacity(expectedFrags);
                        }

                        //Console.WriteLine($"[+] {seqNum}/{expectedFrags}");
                        recvBuff.Add((seqNum, recievedHeader.Data.ToArray()));

                        if(seqNum == expectedFrags) {
                            var sortedList = recvBuff.OrderBy(x => x.Item1).ToList();

                            List<byte> fullMsgData = new List<byte>();
                            foreach(var (seq, data) in sortedList) {
                                fullMsgData.AddRange(data);
                            }

                            string msg = Encoding.ASCII.GetString(fullMsgData.ToArray());
                            Console.WriteLine($">> {msg}");

                            recvBuff.Clear();
                            stopwatch.Reset();
                        }
                        ResetKATimer();

                        break;

                    // 0100 0000 - FILE_MSG_TYPE
                    case 64:
                        seqNum32 = new byte[4];
                        fragTotal32 = new byte[4];

                        seqNum32 = _utils.Create32bit(recievedHeader.SeqNum);
                        fragTotal32 = _utils.Create32bit(recievedHeader.FragTotal);

                        seqNum = BitConverter.ToInt32(seqNum32);

                        #region CRC Check
                        expectedCrc16 = _utils.GetCrc16(recievedHeader.Data.ToArray());
                        if(expectedCrc16 != recievedHeader.Crc16) {
                            MyHeader reply = new MyHeader();
                            reply.Flags = (byte)FlagsEnum.NACK;
                            SendFlags(reply);

                            Console.WriteLine($"[-] Recieved damaged fragment (Seq: {seqNum - 1}), sending again...");
                            continue;
                        }
                        else if(expectedCrc16 == recievedHeader.Crc16 && ACK_RECV != true) {
                            MyHeader reply = new MyHeader();
                            reply.Flags = (byte)FlagsEnum.ACK;
                            SendFlags(reply);
                        }
                        #endregion

                        if(seqNum == 0) {
                            stopwatch.Start();
                            expectedFrags = BitConverter.ToInt32(fragTotal32);
                            recvBuff.EnsureCapacity(expectedFrags);
                            fileName = Encoding.ASCII.GetString(recievedHeader.Data.ToArray());
                        }
                        if(seqNum != 0) {
                            Console.WriteLine($"[+] {seqNum - 1}/{expectedFrags}");
                            recvBuff.Add((seqNum - 1, recievedHeader.Data.ToArray()));

                            if((seqNum - 1) == expectedFrags) {
                                var sortedList = recvBuff.OrderBy(x => x.Item1).ToList();

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
                        ResetKATimer();

                        break;

                    default:
                        Console.WriteLine("[!!!] Hlupost mi prisla, POMOC");
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
            SendFlags(headerToSend);
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
                    SendMessage(msgBytes, false);
                    break;

                case "/dmsg":
                    msgBytes = Encoding.ASCII.GetBytes(message);
                    SendMessage(msgBytes, true);
                    break;

                case "/file":
                    SendFile(message, false);        // message == file path
                    break;

                case "/dfile":
                    SendFile(message, true);
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

                default:
                    Console.WriteLine("[!] Invalid command, use /help if you don't know what to do ;)");
                    break;
            }
        }
    }


    static void SendFlags(MyHeader message) {

        if(message.Data != null) {
            message.Crc16 = _utils.GetCrc16(message.Data.ToArray());
        }

        //lastSent = new MyHeader() {
        //    Flags = message.Flags,
        //    FragTotal = message.FragTotal,
        //    SeqNum = message.SeqNum,
        //    Crc16 = message.Crc16,
        //    Data = message.Data
        //};

        var sendBytes = _utils.GetByteArr(message);
        SendRawBytes(sendBytes);
    }
    static void SendMessage(byte[] message, bool toBeDamaged) {
        bool localToBeDamaged = toBeDamaged;
        List<byte> listMsgBytes = new List<byte>(message);
        var fragments = listMsgBytes.Chunk(FRAGMENT_SIZE).ToList();

        int idx = 0;
        foreach(var frag in fragments) {
            //Console.WriteLine($"[+] SeqNum: {idx}; Size: {frag.Length}");
            try {
                MyHeader fragment = new MyHeader() {
                    Flags = (byte)FlagsEnum.TEXT_MSG_TYPE,
                    FragTotal = idx == 0
                        ? BitConverter.GetBytes(fragments.Count() - 1)
                        : BitConverter.GetBytes(0),
                    SeqNum = BitConverter.GetBytes(idx),
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

                if(localToBeDamaged) {
                    if(idx % MOD == 1) {
                        List<byte> damagedData;
                        damagedData = new List<byte>(fragment.Data);
                        _utils.DamageRandomFragments(damagedData);
                        fragment.Data = damagedData;
                    }
                }

                var sendFragBytes = _utils.GetByteArr(fragment);
                SendRawBytes(sendFragBytes);

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
    static void SendFile(string path, bool toBeDamaged) {
        MyHeader initFragment;

        var fileName = Path.GetFileName(path);
        var nameBytes = Encoding.ASCII.GetBytes(fileName);

        var fileBytes = File.ReadAllBytes(path);
        List<byte> listFileBytes = new List<byte>(fileBytes);
        var fragments = listFileBytes.Chunk(FRAGMENT_SIZE).ToList();

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
        SendRawBytes(sendInitBytes);

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

                if(toBeDamaged) {
                    if(idx % MOD == 1) {
                        List<byte> damagedData;
                        damagedData = new List<byte>(fragment.Data);
                        _utils.DamageRandomFragments(damagedData);
                        fragment.Data = damagedData;
                    }
                }

                var sendFragBytes = _utils.GetByteArr(fragment);
                SendRawBytes(sendFragBytes);

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
        SendRawBytes(sendBytes);
    }
    static void SendRawBytes(byte[] bytes) {
        try {
            sendingClient.Send(bytes, bytes.Length, remoteEndPoint);
        }
        catch(SocketException se) {
            Console.WriteLine("[!] Failed to send packet");
            Console.WriteLine(se.ToString());
        }
    }

    static void OnTimedEvent(Object source, ElapsedEventArgs e) {
        ++KEEP_ALIVE_IDX;

        if(KEEP_ALIVE_IDX > MAX_KEEP_ALIVE_RETRIES) {
            Console.WriteLine("[i] Other peer has disconected, closing the connection");
            sendingClient.Close();
            listenerClient.Close();
            IS_CONNECTION = false;
            return;
        }

        //Console.WriteLine($"[<<] KEEP_ALIVE; KA_idx: {KEEP_ALIVE_IDX}");

        MyHeader kaHeader = new MyHeader() {
            Flags = (byte)FlagsEnum.KEEP_ALIVE
        };
        SendFlags(kaHeader);
    }
    static void ResetKATimer() {
        kaTimer.Stop();
        kaTimer.Start();
        KEEP_ALIVE_IDX = 0;
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
