using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Timers;


namespace USART_Terminal
{
    class Program
    {
        static public string[] FindPorts;
        static SerialPort port = new SerialPort();
        static string log_name;
        static FileStream fs;
        static byte[] _File;
       // static string to_send;
        static string receive;
        enum State
        {
            wait,
            send_reset,
            wait_boot_enter,
            sending_firm,
            calc_all,
            send_com_to_ready,
            entering_boot,
            All_done,
            send_page,
            crc_get,
            page_get,
            calc_all_get,
            addr_get,
        }
        static State state = State.wait;
        static void Main(string[] args)
        {

            Task task = new Task(() => com());
            Task flash = new Task(() => Flash_state_mashine());
            FindPorts = SerialPort.GetPortNames();
            foreach (string s in FindPorts) Console.WriteLine(s);
            Console.WriteLine("Enter port name:");
            port.PortName = Console.ReadLine();
            //Console.WriteLine(port.PortName + " selected, enter baudrate:");
            //port.BaudRate = int.Parse(Console.ReadLine());
            port.BaudRate = 2000000;
            Console.WriteLine(port.BaudRate + " selected");
            port.Open();
            Console.WriteLine(port.PortName + " opened");
            log_name = DateTime.Now.ToString("u");
            log_name = log_name.Replace(".", "");
            log_name = log_name.Replace(":", "");
            log_name = log_name.Replace(" ", "_");
            log_name += ".txt";
            fs = File.Create(log_name);
            _File = File.ReadAllBytes("sensors.bin");
            
            task.Start();
            flash.Start();
            Console.WriteLine(port.PortName + " listening");
            while (true)
            {
                if(state == State.wait)
                    switch (Console.ReadKey().KeyChar)
                    {
                        case 'c':
                            Console.Clear();
                            break;
                        case 'f':
                            Console.WriteLine("starting flash");
                            state = State.entering_boot;
                            break;
                        case 'r':
                            Console.WriteLine("restarting device");
                            port.Write(BitConverter.GetBytes(0xDE), 0, 1);
                            break;
                        case 'j':
                            Console.WriteLine("jump to main app");
                            port.Write(BitConverter.GetBytes(0x01), 0, 1);
                            break;
                    }
            }
        }
        private static Task com()
        {
            while (true)
            {
                byte[] res = new byte[20];
                int[] fin = new int[20];
                int len = 0;
                string reci = null;
                while (true)
                {
                    port.Read(res, len, 1);
                    if (len > 1 && res[len - 1] == 0xDE && res[len] == 0xAD) break;
                    len++;
                }
                len -= 1;
                for (int q = 0; q < len; q += 2)
                {
                    //fin[q/2] = res[q] << 8 | res[q+1];
                    reci += (res[q] << 8 | res[q + 1]).ToString("d") + "; ";
                }
                //foreach (int b in fin)
                //{
                   
                //}
                string outp = DateTime.Now.ToLongTimeString() + ":" + DateTime.Now.Millisecond.ToString() + " " + reci + "\n";
                byte[] bt = System.Text.Encoding.UTF8.GetBytes(outp);
                Console.Write(outp);
                if (log_name != null) fs.Write(bt, 0, bt.Length);
                //else
                //{
                //    receive = port.ReadLine();
                //    string outp = DateTime.Now.ToLongTimeString() + ":" + DateTime.Now.Millisecond.ToString() + ";" + receive + "\n";
                //    byte[] bt = System.Text.Encoding.UTF8.GetBytes(outp);
                //    Console.Write(outp);
                //    if (log_name != null) fs.Write(bt, 0, bt.Length);
                //}
            }
        }
        static public void TimerElapsedEventHandler(object sender, EventArgs args) //проверить таймаут
        {
            state = State.wait;
            Console.WriteLine("Timeout");
        }
        private static Task Flash_state_mashine()
        {
            Timer timer = new Timer(30000);
            ushort shift = 0;
            byte[] to_crc = new byte[1024];
            Crc32 crc32 = new Crc32();
            uint crc;
            timer.Elapsed += new ElapsedEventHandler(TimerElapsedEventHandler);
            while (true)
            {
                switch (state)
                {
                    case State.wait:
                        timer.Stop();
                        break;
                    case State.entering_boot:
                        timer.Start();
                        state = State.send_reset;
                        break;
                    case State.send_reset:
                        port.Write(BitConverter.GetBytes(0x55), 0, 1);
                       // receive = null;
                        state = State.wait_boot_enter;
                        break;
                    case State.wait_boot_enter:
                        if (receive == "Boot")  state = State.send_com_to_ready;
                        break;
                    case State.send_com_to_ready:
                        if (shift * 0x400 < _File.Length)
                        {
                            port.Write(BitConverter.GetBytes(0x02), 0, 1);
                            state = State.sending_firm;
                        }
                        else state = State.All_done;
                        break;
                    case State.sending_firm:
                        if (receive == "ready")
                        {
                            byte[] buf = new byte[4];
                            port.Write(BitConverter.GetBytes(0x08002000 + shift * 0x400), 0, 4);
                            port.Write(BitConverter.GetBytes(0x08002000 + shift * 0x400 + 0x400), 0, 4);
                            state = State.addr_get;
                        }
                        break;
                    case State.addr_get:
                        if (receive == "addr get")
                        {
                            for (int i = 0; i < 1024; i++)
                            {
                                if (i + shift * 0x400 < _File.Length) to_crc[i] = _File[i + shift * 0x400];
                                else to_crc[i] = 0xFF;
                            }
                            crc = crc32.GetCRC32B(to_crc);
                            port.Write(BitConverter.GetBytes(crc), 0, 4);
                            state = State.crc_get;
                        }
                        break;
                    case State.crc_get:
                        if (receive == "crc get")
                        {
                            port.Write(to_crc, 0, 0x400);
                            Console.WriteLine("page " + shift.ToString());
                            state = State.page_get;
                        }
                        break;
                    case State.page_get:
                        if (receive == "Page writed")
                        {
                            shift++;
                            state = State.send_com_to_ready;
                        }
                        break;
                    case State.All_done:
                        Console.WriteLine("final crc calc");
                        port.Write(BitConverter.GetBytes(0x04), 0, 1);
                        state = State.calc_all;
                        break;
                    case State.calc_all:
                        byte[] all_crc = new byte[shift * 0x400];
                        if (receive == "ready")
                        {
                            for (int i = 0; i < shift * 0x400; i++)
                            {
                                if (i < _File.Length) all_crc[i] = _File[i];
                                else all_crc[i] = 0xFF;
                            }
                            crc = crc32.GetCRC32B(all_crc);
                            port.Write(BitConverter.GetBytes(0x08002000), 0, 4);
                            port.Write(BitConverter.GetBytes(0x08002000 + shift * 0x400), 0, 4);
                            port.Write(BitConverter.GetBytes(crc), 0, 4);
                            shift = 0;
                            state = State.wait;
                        }
                        break;
                    default:
                        state = State.wait;
                        break;
                }
            }
        }
    }
    

}
