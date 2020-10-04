using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Collections.Specialized;
using HtmlAgilityPack;
using System.Threading.Tasks;
using System.Globalization;
using CsvHelper;
using System.IO;
using System.Configuration;
using System.Threading;

namespace Scraper
{
    class Program
    {
        private string currentDate = DateTime.Now.ToString("yyyy-MM-dd");
        private string previousDate = DateTime.Now.AddDays(-2).ToString("yyyy-MM-dd");
        private string url = "https://srh.bankofchina.com/search/whpj/searchen.jsp";
        private List<string> currencies = new List<string>();
        private List<Thread> threadPool = new List<Thread>();

        public void Main()
        {
            //Creating list of currencies
            CreateCurrencyList();
            Console.WriteLine();

            int page = 1;

            foreach (var currency in currencies)
            {
                Console.WriteLine($">> Scraping for {currency}...");
                    Thread thread = new Thread(() => { ScrapingForSingleCurrency(currency, page); });
                    threadPool.Add(thread);
                    thread.Start();
                
            }

            while (ThreadFinished());
            Console.WriteLine("\n>> Scraping completed.");
        }
        private bool ThreadFinished()
        {
            foreach (var thread in threadPool)
            {
                if (thread.IsAlive)
                    return true;
            }
            return false;
        }
        private void ScrapingForSingleCurrency(string currency, int page)
        {
            string[] row = new string[7];
            List<string[]> columns = new List<string[]>();

            //First page checkup
            var htmlString = PostHttp(currency, page).Result;
            var html = new HtmlDocument();
            html.LoadHtml(htmlString);
            
            //In case of no records currency is skiped
            HtmlNodeCollection valueNodes = html.DocumentNode.SelectNodes("//td[@class='hui12_20']");
            if (valueNodes.Count < 2)
            {
                //No data for selected currency
                //If needed here could be created an empty csv with appropirate filename
                Console.WriteLine($">> There is no data for {currency}.");
                return;
            }

            //Scraping header row
            HtmlNodeCollection headerNodes = html.DocumentNode.SelectNodes("//td[@bgcolor='#EFEFEF']");
            for (int i = 0; i < 7; i++)
            {
                row[i] = headerNodes[i].InnerText;
            }
            columns.Add(row);
            row = new string[7];

            int count = 0;
            bool hasRecords = true;
            bool hasMorePages = true;
            string[] previousPageRow = new string[7];

            //Iterating through table pages
            while (hasMorePages)
            {
                htmlString = PostHttp(currency, page).Result;
                html = new HtmlDocument();
                html.LoadHtml(htmlString);
                valueNodes = html.DocumentNode.SelectNodes("//td[@class='hui12_20']");

                while (hasRecords)
                {
                    try
                    {
                        for (int i = 0; i < 7; i++)
                        {
                            row[i] = valueNodes[i+count].InnerText;
                        }

                        if (columns.Count == 1)
                        {
                            previousPageRow = row;
                        }

                        if (columns.Count >= 21 && count == 0)
                        {
                            if (SameRowData(previousPageRow, row))
                            {
                                hasMorePages = false;
                                throw new Exception();
                            }
                            else
                            {
                                previousPageRow = row;
                            }
                        }
                        columns.Add(row);
                        row = new string[7];
                        count+=7;

                        if (count == valueNodes.Count)
                            hasRecords = false;
                    }
                    catch (Exception e)
                    {
                        hasRecords = false;
                    }
                }
                count = 0;
                page++;
                hasRecords = true;
            }
            try
            {
                WriteToCsv(columns, currency);
            }
            catch (Exception exc)
            {
                Console.WriteLine(">> Error in csv creation: " + exc.Message);
            }
            Console.WriteLine($">> Data collected and stored in .csv for {currency}.");
        }
        private bool SameRowData(string[] previousPageRow, string[] currentPageRow)
        {
            for (int i = 0; i < 7; i++)
            {
                if (previousPageRow[i] != currentPageRow[i])
                    return false;
            }
            return true;
        }
        private void WriteToCsv(List<string[]> columns, string currency)
        {
            //Folder path can be changed in App.config
            string filePath = ConfigurationManager.AppSettings["Path"];

            using (var writer = new StreamWriter($"{filePath}/{currency}_{previousDate}_{currentDate}"))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                foreach (var column in columns)
                {
                    foreach (var data in column)
                    {
                        csv.Configuration.Delimiter = "\t";
                        csv.WriteField(data);
                    }
                    csv.NextRecord();
                }
            }
        }
        private void CreateCurrencyList()
        {
            string htmlString = "";
            Console.WriteLine(">> Creating currency list...");
            try
            {
                htmlString = GetHttp().Result;
            }
            catch (Exception exc)
            {
                throw new Exception(">> Connection error: Unable to connect.");
            }
            var html = new HtmlDocument();
            html.LoadHtml(htmlString);
            HtmlNodeCollection currenciesNodes = html.DocumentNode.SelectNodes("//option");

            foreach (HtmlNode node in currenciesNodes)
            {
                if (node.Attributes["value"].Value.Contains("0"))
                {
                    continue;
                }
                currencies.Add(node.Attributes["value"].Value);
            }
            Console.WriteLine(">> Currency list created.");
        }
        async Task<string> GetHttp()
        {
            var responseString = "";
            using (var client = new WebClient())
            {
                responseString = client.DownloadString("https://srh.bankofchina.com/search/whpj/searchen.jsp");
            }

            return responseString;
        }
        async Task<string> PostHttp(string currency, int page)
        {
            using (var client = new WebClient())
            {
                var values = new NameValueCollection();
                values["erectDate"] = previousDate;
                values["nothing"] = currentDate;
                values["pjname"] = currency;
                values["page"] = $"{page}";

                var response = client.UploadValues(url, values);
                var responseString = Encoding.Default.GetString(response);

                return responseString;
            }
        }
    }
}
