using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Collections;
using ThoughtWorks.QRCode.Codec;
using ThoughtWorks.QRCode.Codec.Data;
using ThoughtWorks.QRCode.Codec.Util;

namespace Case_QRCode
{
    public partial class Form1 : Form
    {
        bool validData;
        string dragFilename = "";
        CredentialCache myCredentialCache;

        Dictionary<string, string> dictLocation = new Dictionary<string, string>()
        {
            {"datasource=https%253A%252F%252Fexternal.synapse.uscuh.com", "UH/Norris"},
            {"datasource=http%253A%252F%252Fsynapse.uscuh.com", "UH/Norris"},
            {"datasource=https%253A%252F%252Ffujipacs.hsc.usc.edu","HCC2"},
            {"datasource=http%253A%252F%252Fhcc2synvweb","HCC2"},
            {"datasource=http%253A%252F%252Flacsynapse","LACUSC"}
        };


        public Form1()
        {
            InitializeComponent();

            Assembly asm = Assembly.GetExecutingAssembly();
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            DateTime compileDate = new DateTime(v.Build * TimeSpan.TicksPerDay + v.Revision * TimeSpan.TicksPerSecond * 2).AddYears(1999);
            if (TimeZone.IsDaylightSavingTime(compileDate, TimeZone.CurrentTimeZone.GetDaylightChanges(compileDate.Year)))
            {
                compileDate = compileDate.AddHours(1);
            }
            this.labelVersion.Text = String.Format("Build {0}", compileDate);


            cboVersion.SelectedIndex = 7;
            cboCorrectionLevel.SelectedIndex = 0;
            txtSize.Text = "10";

            ServicePointManager.Expect100Continue = false;
            // Ignore Certificate validation failures (aka untrusted certificate + certificate chains)
            ServicePointManager.ServerCertificateValidationCallback = ((sender, certificate, chain, sslPolicyErrors) => true);

            myCredentialCache = new CredentialCache();

        }

        private void imagePanel_DragEnter(object sender, DragEventArgs e)
        {
            if (cbNetwork.Checked)
            {
                MemoryStream ms = e.Data.GetData("UniformResourceLocator") as MemoryStream;
                validData = (ms != null);
            }
            else
            {
                validData = GetFilename(out dragFilename, e);
            }
            e.Effect = DragDropEffects.None;
            if (validData)
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        bool GetFilename(out string filename, DragEventArgs e)
        {
            bool ret = false;
            filename = String.Empty;

            if ((e.AllowedEffect & DragDropEffects.Copy) == DragDropEffects.Copy)
            {
                Array data = e.Data.GetData("FileDrop") as Array;
                if (data != null)
                {
                    if ((data.Length == 1) && (data.GetValue(0) is String))
                    {
                        filename = ((string[])data)[0];
                        ret = true;
                    }
                }
            }
            return ret;
        }

        private void imagePanel_DragDrop(object sender, DragEventArgs e)
        {
            if (!validData) return;
            string mrn = "";
            MemoryStream ms = e.Data.GetData("Synapse.FujiOffset") as MemoryStream;
            StreamReader sr;
            if (ms != null)
            {
                sr = new StreamReader(ms, Encoding.Unicode);
                mrn = sr.ReadToEnd().TrimEnd('\0');
            }
            if (cbNetwork.Checked)
            {
                ms = e.Data.GetData("UniformResourceLocator") as MemoryStream;
                if (ms != null)
                {
                    sr = new StreamReader(ms);
                    string rawstring = sr.ReadToEnd().TrimEnd('\0');
                    //textBoxLine(rawstring);

                    int epath_loc = rawstring.IndexOf("epath=");
                    if (epath_loc != -1)
                    {
                        string epath = DecodeFrom64(rawstring.Substring(epath_loc + 6));
                        epath = epath.Substring(0, epath.LastIndexOf("&") + 1);
                        epath = epath.Replace("&", "%26");
                        epath = epath.Replace("%3A", "%253A");
                        epath = epath.Replace("%2F", "%252F");
                        rawstring = rawstring.Substring(0, epath_loc) + "path=" + epath;
                    }

                    textLoc.Text = mapToLocation(rawstring);
                    string datasource = Regex.Match(rawstring, @"datasource=(.*?)%26").Groups[1].Value;
                    datasource = datasource.Replace("%253A", ":");
                    datasource = datasource.Replace("%252F", "/");

                    //textBoxStatus.Text = "";
                    //textBoxLine(datasource);
                    Uri uriImageURL = new Uri(datasource);

                    string studyUID = Regex.Match(rawstring, @"studyuid=(\d*)").Groups[1].Value;
                    string imageUID = Regex.Match(rawstring, @"imageuid=(\d*)").Groups[1].Value;

                    //textBoxLine("StudyUID = " + studyUID);
                    //textBoxLine("ImageUID = " + imageUID);

                    string uriBase = uriImageURL.GetLeftPart(UriPartial.Authority);

                    Uri uriFujiRDS = new Uri(uriBase + "/SynapseScripts/fujirds.asp");
                    string downloadURL;

                    string querystr;
                    if (datasource.Contains("lacsynapse"))
                    {
                        querystr = @"cmd=select s.http_url||iv.filename as httpfile,
                                               s.https_url||iv.filename as httpsfile,
                                               iv.offset as offset,iv.length as length
                                               from image_version iv, 
                                                 storage s, 
                                                 compression c
                                               where iv.storage_uid=s.id and 
                                                 c.aon_name='Original' and 
                                                 c.id=iv.compression_uid and
                                                 iv.image_uid=" + imageUID + "&";
                    }
                    else
                    {
                        querystr = @"cmd=select s.http_url||iv.filename as httpfile,
                                               s.https_url||iv.filename as httpsfile,
                                               iv.offset as offset,iv.length as length
                                               from image_version iv, 
                                                 storage s, 
                                                 compression c
                                               where iv.storage_uid=s.id and 
                                                 c.aon_name_us='Original' and 
                                                 c.id=iv.compression_uid and
                                                 iv.image_uid=" + imageUID + "&";
                    }


                    byte[] result = retrieveRDS(uriFujiRDS, querystr);
                    if (result == null) return;

                    ADODB.Recordset rs = new ADODB.Recordset();
                    string tempfile = Path.GetTempFileName();
                    ByteArrayToFile(tempfile, result);

                    rs.Open(tempfile, "Provider=MSPersist", ADODB.CursorTypeEnum.adOpenForwardOnly, ADODB.LockTypeEnum.adLockReadOnly, 0);
                    string studyURLhttp = rs.Fields["httpfile"].Value.ToString();
                    string studyURLhttps = rs.Fields["httpsfile"].Value.ToString();
                    long offset = Convert.ToInt64(rs.Fields["offset"].Value);
                    long length = Convert.ToInt64(rs.Fields["length"].Value);
                    rs.Close();
                    File.Delete(tempfile);

                    string studyURL;
                    if (uriBase.StartsWith("https"))
                    {
                        studyURL = studyURLhttps;
                    }
                    else
                    {
                        studyURL = studyURLhttp;
                    }
                    string imageURL = studyURL + "(" + offset + "," + length + ")";
                    //textBoxLine(imageURL);
                    downloadURL = imageURL;
                    string dcmfile = Path.Combine(Path.GetTempPath(), "temp.dcm");

                    WebClient client = new WebClient();
                    client.Credentials = myCredentialCache;
                    client.DownloadFile(downloadURL, dcmfile);
                    //string outputfile = Path.Combine(Path.GetTempPath(), "temp.txt");
                    //Run("dcmdump.exe", "", dcmfile, outputfile);
                    //textBoxLine(File.ReadAllText(outputfile));
                    getDemographics(dcmfile, mrn);
                    File.Delete(dcmfile);
                    //Clipboard.SetText(textBoxStatus.Text);
                }

            }
            else
            {

                string accession = "";
                ms = e.Data.GetData("Synapse.TC") as MemoryStream;
                if (ms != null)
                {
                    sr = new StreamReader(ms, Encoding.Unicode);
                    string[] tcString = sr.ReadToEnd().Trim('\0').Split(',');
                    foreach (string s in tcString)
                    {
                        if (s.StartsWith("A="))
                            accession = s.Substring(2);
                    }
                }
                string imageURL = "";

                ms = e.Data.GetData("UniformResourceLocator") as MemoryStream;
                if (ms != null)
                {
                    sr = new StreamReader(ms);
                    imageURL = sr.ReadToEnd().TrimEnd('\0');

                    int epath_loc = imageURL.IndexOf("epath=");
                    if (epath_loc != -1)
                    {
                        string epath = DecodeFrom64(imageURL.Substring(epath_loc + 6));
                        epath = epath.Substring(0, epath.LastIndexOf("&") + 1);
                        epath = epath.Replace("&", "%26");
                        epath = epath.Replace("%3A", "%253A");
                        epath = epath.Replace("%2F", "%252F");
                        imageURL = imageURL.Substring(0, epath_loc) + "path=" + epath;
                    }

                    textLoc.Text = mapToLocation(imageURL);

                }

                ArrayList results = WebCacheTool.WinInetAPI.FindUrlCacheEntries(@"/\d\.\d.*\)$");
                DateTime latest = DateTime.MinValue;
                DateTime current = DateTime.MinValue;
                string fname = string.Empty;
                if (accession != null)
                {
                    byte[] readBuffer = new byte[4096];
                    byte[] acc = { 0x08, 0x00, 0x50, 0x00, 0x53, 0x48 };
                    string accnum = "";
                    foreach (WebCacheTool.WinInetAPI.INTERNET_CACHE_ENTRY_INFO entry in results)
                    {
                        fname = entry.lpszLocalFileName;
                        using (Stream s = new FileStream(fname, FileMode.Open, FileAccess.Read))
                        {
                            int bytesRead = s.Read(readBuffer, 0, readBuffer.Length);
                            accnum = getDicomString(readBuffer, acc, bytesRead);
                            if (accnum == accession) break;
                        }
                    }
                    if (accnum != accession) fname = "";
                }
                else
                {
                    foreach (WebCacheTool.WinInetAPI.INTERNET_CACHE_ENTRY_INFO entry in results)
                    {
                        current = WebCacheTool.Win32API.FromFileTime(entry.LastAccessTime);
                        if (latest < current)
                        {
                            fname = entry.lpszLocalFileName;
                            latest = current;
                        }

                    }
                }
                if (fname != string.Empty)
                    getDemographics(fname, mrn);
            }

        }

        private string DecodeFrom64(string base64)
        {
            if (base64.Length % 4 == 1) base64 = base64.Substring(0, base64.Length - 1);
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');

            byte[] encodedDataAsBytes = System.Convert.FromBase64String(base64);
            string returnValue = System.Text.ASCIIEncoding.ASCII.GetString(encodedDataAsBytes);
            return returnValue;
        }

        void getDemographics(string fname, string medrecnum)
        {

            Stream s = new FileStream(fname, FileMode.Open, FileAccess.Read);

            byte[] readBuffer = new byte[4096];
            int bytesRead = s.Read(readBuffer, 0, readBuffer.Length);

            byte[] date = { 0x08, 0x00, 0x20, 0x00, 0x44, 0x41 };
            byte[] acc = { 0x08, 0x00, 0x50, 0x00, 0x53, 0x48 };
            byte[] desc = { 0x08, 0x00, 0x30, 0x10, 0x4c, 0x4f };

            byte[] pn = { 0x10, 0x00, 0x10, 0x00, 0x50, 0x4e };
            byte[] mrn = { 0x10, 0x00, 0x20, 0x00, 0x4c, 0x4f };
            byte[] dob = { 0x10, 0x00, 0x30, 0x00, 0x44, 0x41 };
            byte[] mf = { 0x10, 0x00, 0x40, 0x00, 0x43, 0x53 };
            //byte[] loc = { 0x08, 0x00, 0x80, 0x00, 0x4c, 0x4f };

            string dicom_mrn = getDicomString(readBuffer, mrn, bytesRead);
            if ((dicom_mrn == medrecnum) || (dicom_mrn == ""))
            {
                textMRN.Text = medrecnum;
            }
            else
            {
                return;  // we've got the wrong file...
            }

            string rawText;
            rawText = getDicomString(readBuffer, date, bytesRead);
            string studyDate = convertDate(rawText);

            string accnum = getDicomString(readBuffer, acc, bytesRead);
            rawText = getDicomString(readBuffer, desc, bytesRead);
            string studyDesc = rawText.Replace("^", " ");

            rawText = getDicomString(readBuffer, pn, bytesRead);
            textName.Text = rawText.Replace("^", " ");

            rawText = getDicomString(readBuffer, dob, bytesRead);
            textDOB.Text = convertDate(rawText);

            textGender.Text = getDicomString(readBuffer, mf, bytesRead);

            txtEncodeData.Text = textName.Text + " "
                + textMRN.Text + " "
                + studyDesc + " "
                + studyDate + " "
                + textLoc.Text;
            GenerateQRCode();

        }

        string mapToLocation(string URL)
        // map URL to physical location
        {
            foreach (string key in dictLocation.Keys)
            {
                if (URL.Contains(key)) return dictLocation[key];
            }
            return "";
        }

        string convertDate(string strDate)
        {
            if (strDate.Length == 8)
            {
                //reformat to MM-DD-YYYY
                strDate = strDate.Substring(4, 2) + "/" +
                    strDate.Substring(6, 2) + "/" +
                    strDate.Substring(0, 4);
            }
            return strDate;
        }

        string getDicomString(byte[] readBuffer, byte[] target, int bytes)
        {
            int fieldLength;
            System.Text.ASCIIEncoding enc = new System.Text.ASCIIEncoding();

            for (int i = 0; i < bytes - target.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < target.Length; j++)
                    if (target[j] != readBuffer[i + j])
                    {
                        match = false;
                        break;
                    }
                if (match)
                {
                    fieldLength = 256 * readBuffer[i + target.Length + 1] + readBuffer[i + target.Length];
                    byte[] temp = new byte[fieldLength];
                    Array.Copy(readBuffer, i + target.Length + 2, temp, 0, fieldLength);
                    return enc.GetString(temp).Trim().TrimEnd('\0');
                }
            }
            return string.Empty;
        }

        void GenerateQRCode()
        {
            QRCodeEncoder qrCodeEncoder = new QRCodeEncoder();
            qrCodeEncoder.QRCodeEncodeMode = QRCodeEncoder.ENCODE_MODE.BYTE;

            try
            {
                int scale = Convert.ToInt16(txtSize.Text);
                qrCodeEncoder.QRCodeScale = scale;
            }
            catch (Exception)
            {
                MessageBox.Show("Invalid size!");
                return;
            }
            int version = Convert.ToInt16(cboVersion.Text);
            qrCodeEncoder.QRCodeVersion = version;

            string errorCorrect = cboCorrectionLevel.Text;
            if (errorCorrect == "L")
                qrCodeEncoder.QRCodeErrorCorrect = QRCodeEncoder.ERROR_CORRECTION.L;
            else if (errorCorrect == "M")
                qrCodeEncoder.QRCodeErrorCorrect = QRCodeEncoder.ERROR_CORRECTION.M;
            else if (errorCorrect == "Q")
                qrCodeEncoder.QRCodeErrorCorrect = QRCodeEncoder.ERROR_CORRECTION.Q;
            else if (errorCorrect == "H")
                qrCodeEncoder.QRCodeErrorCorrect = QRCodeEncoder.ERROR_CORRECTION.H;

            Image image;
            String data = txtEncodeData.Text;
            image = qrCodeEncoder.Encode(data);
            pb.Image = image;

        }

        private void btnGenerate_Click(object sender, EventArgs e)
        {
            GenerateQRCode();
        }

        private byte[] retrieveRDS(Uri uriRDS, string query)
        {
            string queryEsc = query.Replace(" ", "%20");
            byte[] buffer = new byte[65536];
            byte[] postBytes = Encoding.ASCII.GetBytes(queryEsc);

            Uri baseURI = new Uri(uriRDS.GetLeftPart(UriPartial.Authority));

            while (true)
            {
                try
                {
                    HttpWebRequest myWebRequest = (HttpWebRequest)WebRequest.Create(uriRDS);
                    myWebRequest.Method = "POST";


                    if (myCredentialCache.GetCredential(baseURI, "NTLM") == null)
                    {
                        myCredentialCache.Add(baseURI, "NTLM", CredentialCache.DefaultNetworkCredentials);
                    }
                    myWebRequest.Credentials = myCredentialCache;

                    myWebRequest.ContentType = "application/x-www-form-urlencoded";
                    myWebRequest.ContentLength = postBytes.Length;

                    Stream requestStream = myWebRequest.GetRequestStream();

                    // now send it
                    requestStream.Write(postBytes, 0, postBytes.Length);
                    requestStream.Close();

                    WebResponse webResponse = myWebRequest.GetResponse();
                    using (Stream input = webResponse.GetResponseStream())
                    using (MemoryStream ms = new MemoryStream())
                    {
                        while (true)
                        {
                            int read = input.Read(buffer, 0, buffer.Length);
                            if (read <= 0)
                                return ms.ToArray();
                            ms.Write(buffer, 0, read);
                        }
                    }
                }
                catch (WebException e)
                {
                    if (e.Status == WebExceptionStatus.ProtocolError)
                    {
                        HttpWebResponse httpResponse = (HttpWebResponse)e.Response;
                        int statusCode = (int)httpResponse.StatusCode;
                        //textBoxLine(statusCode + " - " + httpResponse.StatusCode);

                        if (statusCode == 401)
                        {
                            //textBoxLine(e.Response.Headers["WWW-Authenticate"]);

                            Authentication authDialog = new Authentication();
                            authDialog.Reset("Connect to " + baseURI);

                            MessageBoxHelper.PrepToCenterMessageBoxOnForm(this);
                            DialogResult result = authDialog.ShowDialog(this);
                            if (result == DialogResult.OK)
                            {
                                string username = authDialog.username;
                                string domain = "";
                                if (authDialog.username.Contains(@"\"))
                                {
                                    domain = authDialog.username.Split(new char[] { '\\' })[0];
                                    username = authDialog.username.Split(new char[] { '\\' })[1];
                                }
                                myCredentialCache.Remove(baseURI, "Basic");
                                myCredentialCache.Remove(baseURI, "NTLM");
                                myCredentialCache.Add(baseURI, "Basic", new NetworkCredential(username, authDialog.password, domain));
                                myCredentialCache.Add(baseURI, "NTLM", new NetworkCredential(username, authDialog.password, domain));
                                continue;
                            }
                        }
                    }
                    return null;
                }

            }

        }

        public bool ByteArrayToFile(string _FileName, byte[] _ByteArray)
        {
            try
            {
                // Open file for reading
                System.IO.FileStream _FileStream = new System.IO.FileStream(_FileName, System.IO.FileMode.Create, System.IO.FileAccess.Write);

                // Writes a block of bytes to this stream using data from a byte array.
                _FileStream.Write(_ByteArray, 0, _ByteArray.Length);

                // close file stream
                _FileStream.Close();

                return true;
            }
            catch (Exception _Exception)
            {
                // Error
                Console.WriteLine("Exception caught in process: {0}", _Exception.ToString());
            }

            // error occured, return false
            return false;
        }

    }
}
