using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.IO;

namespace Lab08
{
    class Program
    {
        static void Main(string[] args)
        {
            const double mu = 1.0;
            const int channels = 3;
            const int totalRequests = 20;      // per lambda
            const double minLambda = 0.2;
            const double maxLambda = 10.0;
            const double stepLambda = 0.2;

            string csvFile = "results.csv";
            // Write header
            File.WriteAllText(csvFile, "lambda,P0_th,Pn_th,Q_th,A_th,K_th,P0_exp,Pn_exp,Q_exp,A_exp,K_exp\n");

            for (double lambda = minLambda; lambda <= maxLambda + 1e-9; lambda += stepLambda)
            {
                lambda = Math.Round(lambda, 1);
                Console.WriteLine($"--- Simulation for lambda = {lambda}, mu = {mu}, channels = {channels} ---");

                var server = new Server(channels, mu);
                var client = new Client(server);

                // Fire requests
                for (int id = 1; id <= totalRequests; id++)
                {
                    client.Send(id);
                    Console.WriteLine($"Заявка c №{id} поступила на сервер");
                    Thread.Sleep((int)(200.0 / lambda));
                }
                // Wait until processing done
                while (server.GetBusyChannels() > 0)
                    Thread.Sleep(50);

                // Theoretical calculations
                double rho = lambda / mu;
                double P0_th = ComputeP0(rho, channels);
                double Pn_th = ComputePn(rho, channels, P0_th);
                double Q_th = 1 - Pn_th;
                double A_th = lambda * Q_th;
                double K_th = rho * Q_th;

                // Experimental calculations
                double P0_exp = server.FreeTime / server.Time;
                double Pn_exp = (double)server.RejectedCount / server.RequestCount;
                double Q_exp = (double)server.ProcessedCount / server.RequestCount;
                double A_exp = lambda * Q_exp;
                double K_exp = server.BusyTime / (server.Time * mu);

                // Append to CSV (11 values)
                string line = string.Format(
                    "{0},{1:F4},{2:F4},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4}",
                    lambda, P0_th, Pn_th, Q_th, A_th, K_th, P0_exp, Pn_exp, Q_exp, A_exp, K_exp);
                File.AppendAllText(csvFile, line + Environment.NewLine);

                Console.WriteLine($"Logged results for lambda = {lambda}");
            }

            Console.WriteLine("Simulation complete. Results saved to results.csv.");
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }

        static double ComputeP0(double rho, int n)
        {
            double sum = 0;
            for (int k = 0; k <= n; k++) sum += Math.Pow(rho, k) / Factorial(k);
            return 1.0 / sum;
        }

        static double ComputePn(double rho, int n, double P0)
        {
            return Math.Pow(rho, n) / Factorial(n) * P0;
        }

        static double Factorial(int k) => k <= 1 ? 1 : k * Factorial(k - 1);
    }

    public struct PoolRecord
    {
        public Thread Thread;
        public bool InUse;
    }

    public class Server
    {
        private readonly PoolRecord[] pool;
        private readonly object statsLock = new object();
        private readonly DateTime startTime;
        private readonly DateTime[] startTimes;
        private readonly double mu;

        public int RequestCount { get; private set; }
        public int ProcessedCount { get; private set; }
        public int RejectedCount { get; private set; }
        public double BusyTime { get; private set; }
        public double FreeTime { get; private set; }
        public double Time => (DateTime.Now - startTime).TotalSeconds;

        public Server(int channelCount, double serviceRate)
        {
            pool = new PoolRecord[channelCount];
            startTimes = new DateTime[channelCount];
            mu = serviceRate;
            startTime = DateTime.Now;
        }

        public void Proc(object sender, ProcEventArgs e)
        {
            lock (statsLock)
            {
                RequestCount++;
                FreeTime = (DateTime.Now - startTime).TotalSeconds - BusyTime;
                Console.WriteLine($"Заявка c №{e.Id} поступила на сервер");
                if (FindChannel(out int channelIndex))
                {
                    Console.WriteLine($"Заявка №{e.Id} принята в канал {channelIndex + 1}");
                    ProcessRequest(e.Id, channelIndex);
                }
                else
                {
                    RejectedCount++;
                    Console.WriteLine($"Заявка №{e.Id} отклонена");
                }
            }
        }

        private bool FindChannel(out int index)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                if (!pool[i].InUse)
                {
                    index = i;
                    return true;
                }
            }
            index = -1;
            return false;
        }

        private void ProcessRequest(int id, int channelIndex)
        {
            pool[channelIndex].InUse = true;
            startTimes[channelIndex] = DateTime.Now;
            pool[channelIndex].Thread = new Thread(() =>
            {
                Console.WriteLine($"Начата обработка заявки №{id} в канале {channelIndex + 1}");
                Thread.Sleep((int)(1000.0 / mu));
                lock (statsLock)
                {
                    double proc = (DateTime.Now - startTimes[channelIndex]).TotalSeconds;
                    BusyTime += proc;
                    pool[channelIndex].InUse = false;
                    ProcessedCount++;
                    Console.WriteLine($"Заявка №{id} обработана в канале {channelIndex + 1}");
                }
            });
            pool[channelIndex].Thread.Start();
        }

        public int GetBusyChannels() => pool.Count(r => r.InUse);
    }

    public class Client
    {
        public event EventHandler<ProcEventArgs> Request;
        public Client(Server server) => Request += server.Proc;
        public void Send(int id) => Request?.Invoke(this, new ProcEventArgs { Id = id });
    }

    public class ProcEventArgs : EventArgs { public int Id { get; set; } }
}
