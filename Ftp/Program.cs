using Xyz.Olooko.Comm.Ftp;

namespace Ftp
{
    internal class Program
    {
        static long transferredSize = 0;

        static AutoResetEvent event_1 = new AutoResetEvent(false);

        static void Main(string[] args)
        {
            FtpClient client = FtpComm.FtpConnect("192.168.2.73", 21);

            if (!client.Connected)
                return;

            Console.WriteLine(client.WelcomeMessage);

            if (!client.Login("ftpuser", "1234"))
                return;

            Console.WriteLine("Connected to Server!!");
            Console.WriteLine(client.PrintWorkingDirectory());

            while (true)
            {
                Console.WriteLine("");
                Console.WriteLine("Command List:");
                Console.WriteLine("\t[0] Change Working Directory");
                Console.WriteLine("\t[1] Make Directory");
                Console.WriteLine("\t[2] Remove Directory");
                Console.WriteLine("\t[3] Download File");
                Console.WriteLine("\t[4] Upload File");
                Console.WriteLine("\t[5] Delete File");
                Console.WriteLine("\t[6] Rename");

                Console.Write("Input Command Number: ");
                string command = Console.ReadLine();

                if (command == "0")
                {
                    Console.Write("Directory Name: ");
                    if (client.ChangeWorkingDirectory(Console.ReadLine()))
                        Console.WriteLine(client.PrintWorkingDirectory());
                }
                else if (command == "1")
                {
                    Console.Write("Directory Name: ");
                    client.MakeDirectory(Console.ReadLine());
                }
                else if (command == "2")
                {
                    Console.Write("Directory Name: ");
                    client.RemoveDirectory(Console.ReadLine());
                }
                else if (command == "3")
                {
                    Console.Write("File Name: ");
                    string name = Console.ReadLine();

                    FileInfo fi = new FileInfo(String.Format(@"download\{0}", name));
                    transferredSize = 0;

                    if (fi.Exists)
                        transferredSize = fi.Length;

                    client.DownloadFile(name, fi, FtpProgressCallback, transferredSize);
                    event_1.WaitOne();
                }
                else if (command == "4")
                {
                    Console.Write("File Path: ");
                    string path = Console.ReadLine();

                    FileInfo fi = new FileInfo(path);

                    transferredSize = 0;// client.GetFileSize(fi.Name);

                    client.UploadFile(fi, fi.Name, FtpProgressCallback, transferredSize);
                    event_1.WaitOne();
                }
                else if (command == "5")
                {
                    Console.Write("File Name: ");
                    client.DeleteFile(Console.ReadLine());
                }
                else if (command == "6")
                {
                    Console.Write("From: ");
                    string from = Console.ReadLine();
                    Console.Write("To: ");
                    string to = Console.ReadLine();
                    client.Rename(from, to);
                }
            }
        }

        static void FtpProgressCallback(FtpDataProgressEventArgs e)
        {
            if (e.DataSize > 0)
            {
                transferredSize += e.DataSize;
                Console.Write("\r{0} - {1:N0}%", e.FileName, ((double)transferredSize / e.FileSize) * 100);
            }
            else
            {
                if (e.ResultMessage != String.Empty)
                    Console.WriteLine("\r\n{0}", e.ResultMessage);

                event_1.Set();
            }
        }

    }
}

