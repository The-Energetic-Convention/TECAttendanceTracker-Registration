using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO.Ports;
using System.Diagnostics;
using System.Threading;
using Sydesoft.NfcDevice;

namespace AttendanceTrackerRegistration
{
    public partial class MainForm : Form
    {
        HttpClient client = new HttpClient { BaseAddress = new Uri("http://10.0.0.216:6969") };
        private string authKey = Environment.GetEnvironmentVariable("TECMasterAuth") ?? "NULL!";
        ACR122U Reader = new ACR122U();

        bool readyForCard = false;
        bool readyToRegister = false;
        bool writeDone = true;
        string NameToWrite = "";
        int IDToWrite = 0;

        public MainForm()
        {
            InitializeComponent();
            Reader.Init(false, 50, 4, 4, 200);
            Reader.CardInserted += WriteBadge;
            Reader.CardRemoved += BadgeRemoved;
        }

        private async void RegisterButton_Click(object sender, EventArgs e)
        {
            new Thread(Register).Start();
        }

        private async void Register()
        {
            //check to make sure a name is entered lol
            if (AttendeeName.Text == "") { StatusText.Invoke((MethodInvoker)delegate { StatusText.Text = "No Name Entered!"; }); return; }

            StatusText.Invoke((MethodInvoker)delegate { StatusText.Text = "Checking..."; });

            // Make a request to the server to get the list of attendees
            string attendeeListString = await client.GetStringAsync("/GetAttendee");
            //StatusText.Invoke((MethodInvoker)delegate { StatusText.Text = attendeeListString; });
            // Check to see if this one is already registered
            Attendee[] attendeeList = JsonConvert.DeserializeObject<Attendee[]>(attendeeListString);
            try
            {
                // if this gives an exception, the attendee name was not found
                // if it doesnt, they were found, say so and return, no need to continue registration
                Attendee found = attendeeList.Single(attendee => attendee.Name == AttendeeName.Text);
                AttendeeName.Invoke((MethodInvoker)delegate { AttendeeName.Text = ""; });
                StatusText.Invoke((MethodInvoker)delegate { StatusText.Text = "Attendee Name Already Registered!"; });
                return;
            }
            catch { }
            //StatusText.Invoke((MethodInvoker)delegate { StatusText.Text = "Not found"; });

            //wait for badge writing, easier to rewrite the badge if the reqest fails, than delete the registration if a write fails
            int newID = attendeeList[attendeeList.Length - 1].ID + 1;
            NameToWrite = AttendeeName.Text;
            IDToWrite = newID;
            readyForCard = true;
            StatusText.Invoke((MethodInvoker)delegate { StatusText.Text = "Scan Tag"; });

            Console.WriteLine("spin spin spin spin");
            while (!readyToRegister) { /* Camellia - SPIN ETERNALLY starts playing */ }

            StatusText.Invoke((MethodInvoker)delegate { StatusText.Text = "Registering..."; });

            // If not registered, make a request to register
            string result = await client.PostAsync($"/RegisterAttendee?Name={AttendeeName.Text}&AtCon={AtCon.Checked}", new MultipartFormDataContent{
                    new StringContent(authKey) { Headers = {
                        ContentDisposition =
                            new ContentDispositionHeaderValue("form-data") { Name = "Auth"} } } })
                                .Result.Content.ReadAsStringAsync();
            if (result != "SUCCESS") { StatusText.Invoke((MethodInvoker)delegate { StatusText.Text = $"Error: {result}"; }); return; }

            // Make a request to get the Attendee, to check successful registration
            string registeredAttendeeString = await client.GetStringAsync($"/GetAttendee?ID={attendeeList.Last().ID + 1}");
            if (registeredAttendeeString == "[null]") { StatusText.Invoke((MethodInvoker)delegate { StatusText.Text = "Error Registering Attendee"; }); return; }
            Attendee registeredAttendee = JsonConvert.DeserializeObject<Attendee[]>(registeredAttendeeString)[0];

            StatusText.Invoke((MethodInvoker)delegate { StatusText.Text = "Done!"; });
            readyToRegister = false;
            Thread.Sleep(1000);

            //reset ui
            StatusText.Invoke((MethodInvoker)delegate { StatusText.Text = "Enter Info"; });
            AttendeeName.Invoke((MethodInvoker)delegate { AttendeeName.Text = ""; });
        }

        void WriteBadge(PCSC.ICardReader reader)
        {
            if (readyForCard) {
                writeDone = false;
                readyForCard = false;
                byte[] data = { 0x03, 0x25, 0xD1, 0x01, 0x21, 0x55, 0x00};
                string toWrite = $"attendee://Name={NameToWrite}&ID={IDToWrite}";
                bool result = Reader.WriteData(reader, data.Concat(Encoding.UTF8.GetBytes(toWrite)).ToArray());
                if (!result) { StatusText.Invoke((MethodInvoker)delegate { StatusText.Text = "Error Writing badge"; }); }
                string check = Encoding.UTF8.GetString(Reader.ReadData(reader));
                if (check != toWrite) { StatusText.Invoke((MethodInvoker)delegate { StatusText.Text = "Error Writing Badge"; }); }
                writeDone = true;
                readyToRegister = true;
                return;
            }
            StatusText.Invoke((MethodInvoker)delegate { StatusText.Text = "Not Ready For Badge"; });
        }

        void BadgeRemoved()
        {
            if (!writeDone)
            {
                StatusText.Invoke((MethodInvoker)delegate
                {
                    StatusText.Text = "Badge Removed Early!\nPlease wait for writing to be done!";
                });
            }
        }
    }

    public class Attendee
    {
        public Attendee(string name, int iD, bool atCon, List<DateTime> joinDates, List<DateTime> leaveDates)
        {
            Name = name;
            ID = iD;
            AtCon = atCon;
            JoinDates = joinDates;
            LeaveDates = leaveDates;
        }

        public string Name { get; set; }
        public int ID { get; set; }
        public bool AtCon { get; set; }
        public List<DateTime> JoinDates { get; set; }
        public List<DateTime> LeaveDates { get; set; }
    }
}