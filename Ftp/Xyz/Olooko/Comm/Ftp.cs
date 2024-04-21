using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace Xyz.Olooko.Comm.Ftp
{
    public class FtpComm
    {
        public static FtpClient FtpConnect(string address, int port)
        {
            Socket s = new Socket(SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint iep = new IPEndPoint(IPAddress.Parse(address), port);
            s.Connect(iep);

            return new FtpClient(s);
        }
    }

    public class FtpDataProgressEventArgs
    {
        public string FileName { get; }
        public long FileSize { get; }
        public long DataSize { get; }
        public string ResultMessage { get; }

        public FtpDataProgressEventArgs(string fileName, long fileSize, long dataSize, string resultMessage)
        {
            this.FileName = fileName;
            this.FileSize = fileSize;
            this.DataSize = dataSize;
            this.ResultMessage = resultMessage;
        }
    }

    public class FtpClient
    {
        private enum FtpDataProgressType
        {
            Download, Upload
        }

        private class FtpDataProgressState
        {
            public FtpDataProgressType FileProgressType { get; }
            public string FileName { get; }
            public FileInfo LocalFile { get; }
            public long FileSize { get; }
            public Action<FtpDataProgressEventArgs> ProgressCallback { get; }
            public long TransferredSize { get; }

            public FtpDataProgressState(FtpDataProgressType progressType, string fileName, FileInfo localFile, 
                long fileSize, Action<FtpDataProgressEventArgs> progressCallback, long transferredSize)
            {
                this.FileProgressType = progressType;
                this.FileName = fileName;
                this.LocalFile = localFile;
                this.FileSize = fileSize;
                this.ProgressCallback = progressCallback;
                this.TransferredSize = transferredSize;
            }
        }

        public class FtpResponse
        {
            public string Code { get; }
            public string Message { get; }

            public FtpResponse(string code, string message)
            {
                this.Code = code;
                this.Message = message;
            }
        }

        private Socket _command;
        private string _welcome;

        public bool Connected
        {
            get { return _command.Connected; }
        }

        public string WelcomeMessage
        {
            get { return _welcome; }
        }

        public FtpClient(Socket s)
        {
            _command = s;
            _welcome = String.Empty;

            FtpResponse response = GetResponse();
            if (response.Code == "220") _welcome = response.Message;
        }

        public bool Login(string username, string password)
        {
            FtpResponse response = null;
            
            response = SetCommand("USER {0}", username);
            if (response.Code != "331") return false;

            response = SetCommand("PASS {0}", password);
            return (response.Code == "230") ? true : false;
        }

        public string PrintWorkingDirectory()
        {
            FtpResponse response = SetCommand("PWD");

            string wd = String.Empty;

            if (response.Code == "257")
            {
                int start = response.Message.IndexOf("\"");
                int end = response.Message.LastIndexOf("\"");

                wd = response.Message.Substring(start + 1, end - start - 1);
            }
            return wd;
        }

        public bool ChangeWorkingDirectory(string name)
        {
            FtpResponse response = SetCommand("CWD {0}", name);
            return (response.Code == "250") ? true : false;
        }

        public bool Rename(string fromName, string toName)
        {
            FtpResponse response = null;
            
            response = SetCommand("RNFR {0}", fromName);
            if (response.Code != "350") return false;

            response = SetCommand("RNTO {0}", toName);
            return (response.Code == "250") ? true : false;
        }

        public long GetFileSize(string name)
        {
            FtpResponse response = SetCommand("SIZE {0}", name);

            if (response.Code != "213") return 0;
            return Int64.Parse(response.Message);
        }

        public bool MakeDirectory(string name)
        {
            FtpResponse response = SetCommand("MKD {0}", name);
            return (response.Code == "257") ? true : false;
        }

        public bool RemoveDirectory(string name)
        {
            FtpResponse response = SetCommand("RMD {0}", name);
            return (response.Code == "250") ? true : false;
        }

        public bool DeleteFile(string name)
        {
            FtpResponse response = SetCommand("DELE {0}", name);
            return (response.Code == "250") ? true : false;
        }

        public void DownloadFile(string fileName, FileInfo localFile, Action<FtpDataProgressEventArgs> progressCallback, long transferredSize = 0)
        {
            long size = GetFileSize(fileName);
            FtpDataProgressState state = new FtpDataProgressState(FtpDataProgressType.Download, fileName, localFile, size, progressCallback, transferredSize);

            Thread t = new Thread(new ParameterizedThreadStart(DataProgressProc));
            t.IsBackground = true;
            t.Start(state);
        }

        public void UploadFile(FileInfo localFile, string fileName, Action<FtpDataProgressEventArgs> progressCallback, long transferredSize = 0)
        {
            if (localFile.Exists)
            {
                long size = localFile.Length;
                FtpDataProgressState state = new FtpDataProgressState(FtpDataProgressType.Upload, fileName, localFile, size, progressCallback, transferredSize);

                Thread t = new Thread(new ParameterizedThreadStart(DataProgressProc));
                t.IsBackground = true;
                t.Start(state);
            }
        }

        private void DataProgressProc(object state)
        {
            FtpResponse response = SetCommand("PASV");

            int start = response.Message.IndexOf("(");
            int end = response.Message.LastIndexOf(")");

            string[] addrInfo = response.Message.Substring(start + 1, end - start - 1).Split(',');

            string host = string.Format("{0}.{1}.{2}.{3}", addrInfo[0], addrInfo[1], addrInfo[2], addrInfo[3]);
            int port = Int32.Parse(addrInfo[4]) * 256 + Int32.Parse(addrInfo[5]);

            Socket data = new Socket(SocketType.Stream, ProtocolType.Tcp);
            data.Connect(host, port);

            if (data.Connected)
            {
                FtpDataProgressState fdpState = state as FtpDataProgressState;
                FileStream fs = null;

                switch (fdpState.FileProgressType)
                {
                    case FtpDataProgressType.Download:
                        if (fdpState.TransferredSize > 0)
                        {
                            SetCommand("REST {0}", fdpState.TransferredSize);
                            fs = fdpState.LocalFile.Open(FileMode.Append, FileAccess.Write);
                        }
                        else
                        {
                            fs = fdpState.LocalFile.Open(FileMode.OpenOrCreate, FileAccess.Write);
                        }
                        SetCommand("RETR {0}", fdpState.FileName);                       
                        break;

                    case FtpDataProgressType.Upload:
                        fs = fdpState.LocalFile.OpenRead();
                        if (fdpState.TransferredSize > 0)
                        {
                            SetCommand("REST {0}", fdpState.TransferredSize);
                            fs.Position = fdpState.TransferredSize;
                        }
                        SetCommand("STOR {0}", fdpState.FileName);                     
                        break;
                }

                byte[] buffer = new byte[4096];

                int length = -1;

                while (length != 0)
                {
                    if (fdpState.FileProgressType == FtpDataProgressType.Download)
                    {
                        length = data.Receive(buffer, 0, buffer.Length, SocketFlags.None);

                        if (length > 0)
                        {
                            fs.Write(buffer, 0, length);
                            fs.Flush();
                        }
                    }
                    else if (fdpState.FileProgressType == FtpDataProgressType.Upload)
                    {
                        length = fs.Read(buffer, 0, buffer.Length);

                        if (length > 0)
                        {
                            int bytesSend = 0;
                            while (bytesSend < length)
                                bytesSend += data.Send(buffer, bytesSend, length - bytesSend, SocketFlags.None);
                        }
                    }

                    string n = fdpState.FileName;
                    long t = fdpState.FileSize;
                    long s = (long)length;
                    string m = String.Empty;

                    if (length == 0)
                    {
                        fs.Close();
                        data.Close();

                        m = GetResponse().Message;
                    }

                    fdpState.ProgressCallback(new FtpDataProgressEventArgs(n, t, s, m));
                }
            }
        }

        private FtpResponse SetCommand(string format, params object[] args)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(string.Format(format, args) + "\r\n");
            int length = 0;

            while (length < bytes.Length)
                length += _command.Send(bytes, length, bytes.Length - length, SocketFlags.None);

            return GetResponse();
        }

        private FtpResponse GetResponse()
        {
            byte[] buffer = new byte[200];
            int pos = 0;
            string code = String.Empty;
            string message = String.Empty;

            while (true)
            {
                _command.Receive(buffer, pos, 1, SocketFlags.None);

                if (buffer[pos++] == 0x0A)
                {
                    string s = Encoding.UTF8.GetString(buffer, 0, pos).Replace("\r", "").Replace("\n", "");
                    string[] ss = s.Split(" ".ToCharArray(), 2);
                    code = ss[0];
                    message = ss[1];

                    if (code.Length != 3 || !int.TryParse(code, out _))
                    {
                        Buffer.BlockCopy(buffer, pos, buffer, 0, 200 - pos);
                        pos = 0;
                    }
                    else
                        break;
                }
            }

            return new FtpResponse(code, message);
        }
    }
}
