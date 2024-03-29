﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Web;

using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Collections;
using ThoughtWorks.QRCode.Codec;

using Synapse5Lib;


namespace RadQRCode
{
    public partial class Form1 : Form
    {
        bool validData;
        string dragFilename = "";
        bool preventGenerateCode = false;
        CredentialCache myCredentialCache;
        Synapse5 syn;

        Dictionary<string, string> dictLocation = new Dictionary<string, string>()
        {
            {"https://keckimaging.med.usc.edu", "Keck/Norris"},
            {"https://external.synapse.uscuh.com", "Keck/Norris"},
            {"http://synapse.uscuh.com", "Keck/Norris"},
            {"https://fujipacs.hsc.usc.edu","HCC2"},
            {"http://hcc2synvweb","HCC2"},
            {"http://lacsynapse","LACUSC"},
            {"http://dhssynapse","LACUSC"}
        };


        public Form1()
        {
            InitializeComponent();

            Assembly asm = Assembly.GetExecutingAssembly();

            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            DateTime compileDate = new DateTime(2000, 1, 1).Add(new TimeSpan(v.Build * TimeSpan.TicksPerDay + v.Revision * TimeSpan.TicksPerSecond * 2));
            if (TimeZone.IsDaylightSavingTime(compileDate, TimeZone.CurrentTimeZone.GetDaylightChanges(compileDate.Year)))
            {
                compileDate = compileDate.AddHours(1);
            }

            this.labelVersion.Text = String.Format("Build {0}", compileDate);


            cboVersion.SelectedIndex = 7;
            cboCorrectionLevel.SelectedIndex = 0;
            txtSize.Text = "10";

            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;  // activate TLS 1.2
            ServicePointManager.Expect100Continue = false;
            // Ignore Certificate validation failures (aka untrusted certificate + certificate chains)
            ServicePointManager.ServerCertificateValidationCallback = ((sender, certificate, chain, sslPolicyErrors) => true);

            myCredentialCache = new CredentialCache();
            syn = new Synapse5();

        }

        private void imagePanel_DragEnter(object sender, DragEventArgs e)
        {
            if (cbNetwork.Checked)
            {
                MemoryStream ms = e.Data.GetData("UniformResourceLocator") as MemoryStream;
                validData = (ms != null);
                if (!validData)
                {
                    validData = e.Data.GetDataPresent("Text");
                }
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
            string medrecnum = "";
            MemoryStream ms = e.Data.GetData("Synapse.FujiOffset") as MemoryStream;
            StreamReader sr;
            if (ms != null)
            {
                sr = new StreamReader(ms, Encoding.Unicode);
                medrecnum = sr.ReadToEnd().TrimEnd('\0');
            }

            ms = e.Data.GetData("UniformResourceLocator") as MemoryStream;
            if (!e.Data.GetDataPresent("Text") && (ms == null)) // Not a Synapse drag event
            {
                Array data = e.Data.GetData("FileDrop") as Array;
                if ((data != null) && (data.GetValue(0) is String))
                {
                    string dcmfile = ((string[])data)[0];
                    if (File.Exists(dcmfile))
                    {
                        getDemographics(dcmfile, medrecnum, true, false);
                        return;
                    }
                }
            }

            if (cbNetwork.Checked)
            {
                string datasource = "";
                string querystr = "";
                if (e.Data.GetDataPresent("Text"))
                {
                    datasource = (String)e.Data.GetData("Text");
                    System.Collections.Specialized.NameValueCollection qscoll = HttpUtility.ParseQueryString(datasource);
                    String studyUID = qscoll.Get("studyUID");
                    String MRN = qscoll.Get("DDsrcPatientMRN");
                    String source = qscoll.Get("dataSource");
                    Uri site = new Uri(datasource);
                    string url = site.GetLeftPart(UriPartial.Authority);
                    if (!syn.SetAccessToken(url))
                    {
                        syn.SetAccessTokenEnvironment(url);
                    }
                    dynamic result = syn.GetWFStudyDetails(studyUID, source);
                    String description = result.ProcedureDescription;

                    DateTime dt = result.PerformedDate;
                    if ((MRN!=null) && (dt!=null) && (description!=null))
                    {
                        preventGenerateCode = true;
                        textMRN.Text = MRN;
                        textStudy.Text = description;
                        textDate.Text = dt.ToString("yyyy-MM-dd");
                        textDesc.Text = "";
                        textLoc.Text = mapToLocation(datasource);
                        EncodeData();
                        preventGenerateCode = false;
                        return;
                    }

                    String objectUID = qscoll.Get("objectUID");
                    if (objectUID == null)
                    {
                        objectUID = qscoll.Get("DDFirstSOPInstanceUID");
                    }
                    if (objectUID != null)
                    {
                        querystr = @"select p.external_eid as mrn, 
s.ris_study_euid as accessionnumber, 
description, 
study_timedate 
from patient p, 
procedure_info pi,
study s,
image i 
where pi.id=s.procedure_info_uid and p.id=
s.patient_uid and s.id=i.study_uid and i.sop_instance_euid='" + objectUID+"'";
                        textLoc.Text = mapToLocation(datasource);
                    }
                }
                if (querystr == "")
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

                        datasource = Regex.Match(rawstring, @"datasource=(.*?)%26").Groups[1].Value;

                        datasource = datasource.Replace("%253A", ":");
                        datasource = datasource.Replace("%252F", "/");
                        textLoc.Text = mapToLocation(datasource);


                        string studyUID = Regex.Match(rawstring, @"studyuid=(\d*)").Groups[1].Value;
                        string imageUID = Regex.Match(rawstring, @"imageuid=(\d*)").Groups[1].Value;


                        querystr = @"select p.external_eid as mrn, 
s.ris_study_euid as accessionnumber, 
description, 
study_timedate 
from patient p, 
procedure_info pi,
study s,
image i 
where pi.id=s.procedure_info_uid and p.id=
s.patient_uid and s.id=i.study_uid and i.id=" + imageUID;
                    }
                }
                if (querystr!="") {
                    Uri uriImageURL = new Uri(datasource);
                    string uriBase = uriImageURL.GetLeftPart(UriPartial.Authority);
                    Uri uriFujiRDS = new Uri(uriBase + "/SynapseScripts/fujirds.asp");

                    byte[] result = retrieveRDS(uriFujiRDS, querystr);
                    if (result == null) return;

                    ADODB.Recordset rs = new ADODB.Recordset();
                    string tempfile = Path.GetTempFileName();
                    ByteArrayToFile(tempfile, result);

                    rs.Open(tempfile, "Provider=MSPersist", ADODB.CursorTypeEnum.adOpenForwardOnly, ADODB.LockTypeEnum.adLockReadOnly, 0);

                    string description = rs.Fields["description"].Value.ToString();
                    DateTime dt = DateTime.Parse(rs.Fields["study_timedate"].Value.ToString());
                    string mrn = rs.Fields["mrn"].Value.ToString();
                    
                    rs.Close();
                    File.Delete(tempfile);
                    textDesc.Text = "";
                    textStudy.Text = description;
                    textMRN.Text = mrn;
                    textDate.Text = dt.ToString("yyyy-MM-dd");


                }
            }
            else
            {
                string studyUID = "";
                ms = e.Data.GetData("Synapse.TC") as MemoryStream;
                if (ms != null)
                {
                    sr = new StreamReader(ms, Encoding.Unicode);
                    string[] tcString = sr.ReadToEnd().Trim('\0').Split(',');
                    foreach (string s in tcString)
                    {
                        if (s.StartsWith("S="))
                            studyUID = s.Substring(2);
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
                ArrayList prefixes = new ArrayList();
                SortedList<DateTime, String> slist = new SortedList<DateTime, String>();

                foreach (WebCacheTool.WinInetAPI.INTERNET_CACHE_ENTRY_INFO entry in results)
                {
                    DateTime dt = WebCacheTool.Win32API.FromFileTime(entry.LastAccessTime);
                    if ((DateTime.Now - dt) > TimeSpan.FromHours(24)) continue;
                    string fname = entry.lpszLocalFileName;
                    int index = fname.IndexOf('(');
                    if (index > 0)
                    {
                        string prefix = fname.Substring(0, index);
                        if (prefixes.Contains(prefix) || slist.ContainsKey(dt)) continue;
                        prefixes.Add(prefix);
                        slist.Add(dt, fname);
                    }
                }
              
                byte[] readBuffer = new byte[65536];
                int bytesRead;

                byte[] mrn = { 0x10, 0x00, 0x20, 0x00, 0x4c, 0x4f };
                byte[] sid = { 0x20, 0x00, 0x0d, 0x00, 0x55, 0x49 };
                string test_sid = "not found";
                string dicom_mrn = "";
                string fname_match = "";

                if (studyUID!="")
                {
                    for (int i = slist.Count - 1; i >= 0; i--)
                    {
                        fname_match = slist.Values[i];
                        using (Stream s = new FileStream(fname_match, FileMode.Open, FileAccess.Read))
                        {
                            bytesRead = s.Read(readBuffer, 0, readBuffer.Length);
                            test_sid = getDicomString(readBuffer, sid, bytesRead);
                            if (test_sid == studyUID) break;
                        }
                    }
                    if (test_sid != studyUID)
                    {
                        fname_match = "";
                    }
                }

                if ((fname_match=="") && (medrecnum!=""))
                {
                    for (int i = slist.Count - 1; i >= 0; i--)
                    {
                        fname_match = slist.Values[i];
                        using (Stream s = new FileStream(fname_match, FileMode.Open, FileAccess.Read))
                        {
                            bytesRead = s.Read(readBuffer, 0, readBuffer.Length);
                            dicom_mrn = getDicomString(readBuffer, mrn, bytesRead);
                            if (dicom_mrn == medrecnum) break;
                        }
                    }
                    if (dicom_mrn != medrecnum)
                    {
                        return;
                    }
                }

                if (fname_match!="") getDemographics(fname_match, medrecnum, test_sid==studyUID, true);
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

        void getDemographics(string fname, string medrecnum, bool correct_study_id, bool synapse_event)
        {
            preventGenerateCode = true;

            Stream s = new FileStream(fname, FileMode.Open, FileAccess.Read);

            byte[] readBuffer = new byte[65536];
            int bytesRead = s.Read(readBuffer, 0, readBuffer.Length);

            byte[] date = { 0x08, 0x00, 0x20, 0x00, 0x44, 0x41 };
            //byte[] acc = { 0x08, 0x00, 0x50, 0x00, 0x53, 0x48 };
            byte[] desc = { 0x08, 0x00, 0x30, 0x10, 0x4c, 0x4f };

            byte[] mrn = { 0x10, 0x00, 0x20, 0x00, 0x4c, 0x4f };
            byte[] loc = { 0x08, 0x00, 0x80, 0x00, 0x4c, 0x4f };

            //string dicom_mrn = getDicomString(readBuffer, mrn, bytesRead);
            //if ((synapse_event) && (dicom_mrn!= medrecnum) && (dicom_mrn!=""))
            //{
            //    return;  // we've got the wrong file...
            //}
            //textMRN.Text=dicom_mrn;
            textMRN.Text = medrecnum;

            string rawText = "";

            if (!synapse_event)
            {
                textLoc.Text = getDicomString(readBuffer, loc, bytesRead);
            }
            
            //string accnum = getDicomString(readBuffer, acc, bytesRead);
            //string studyDate = "";
            //string studyDesc = "";
            if (correct_study_id)
            {
                rawText = getDicomString(readBuffer, date, bytesRead);
                textDate.Text= convertDate(rawText);
                rawText = getDicomString(readBuffer, desc, bytesRead);
                textStudy.Text = rawText.Replace("^", " ");
            }

            textDesc.Text = "";
            EncodeData();

            preventGenerateCode = false;
        }

        private void EncodeData()
        {
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
                //reformat to YYYY-MM-DD
                strDate = strDate.Substring(0, 4) +"-"+
                    strDate.Substring(4, 2) + "-" +
                    strDate.Substring(6, 2);
            }
            return strDate;
        }

        string getDicomString(byte[] readBuffer, byte[] target, int bytes)
        {
            string match = matchString(readBuffer, target, bytes, true);
            if (match != string.Empty) return match;
            return matchString(readBuffer, target, bytes, false);

        }

        string matchString(byte[] readBuffer, byte[] target, int bytes, bool littleEndian)
        {
            int fieldLength;
            System.Text.ASCIIEncoding enc = new System.Text.ASCIIEncoding();

            if (!littleEndian)
            {
                byte b = target[0];
                target[0] = target[1];
                target[1] = b;
                b = target[2];
                target[2] = target[3];
                target[3] = b;
            }

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
                    if (littleEndian)
                    {
                        fieldLength = 256 * readBuffer[i + target.Length + 1] + readBuffer[i + target.Length];
                    }
                    else
                    {
                        fieldLength = 256 * readBuffer[i + target.Length] + readBuffer[i + target.Length+1];
                    }
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
            try
            {
                string[] stringList ={textLoc.Text, textMRN.Text, textStudy.Text, textDate.Text, 
                                         textDesc.Text, checkBoxFollow.Checked?"1":"0"};
                for (int i = 0; i < stringList.Length; i++)
                {
                    stringList[i] = Regex.Replace(stringList[i], @"[|]", "");
                }
                if (string.Join("", stringList) == "0")
                {
                    // empty case
                    pb.Image = null;
                    textEncode.Text = "";
                }
                else
                {
                    String data = string.Join("|", stringList);
                    image = qrCodeEncoder.Encode(data);
                    pb.Image = image;
                    textEncode.Text = data;
                }
                toolStripLabel.Text = "";
            }
            catch (System.IndexOutOfRangeException)
            {
                toolStripLabel.Text = "Error: the text is too long for generating a QRCode!";
            }
            catch (Exception e)
            {
                toolStripLabel.Text = e.Message.ToString();
            }
            

        }

        private void btnGenerate_Click(object sender, EventArgs e)
        {
            EncodeData();
        }

        private byte[] retrieveRDS(Uri uriRDS, string query)
        {
            byte[] encodeQuery = System.Text.Encoding.Unicode.GetBytes(query + "uguessit");
            System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create();
            byte[] hashBytes = md5.ComputeHash(encodeQuery);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("X2"));
            }
            string signature = "signature=" + sb.ToString() + "&";

            query = signature + "cmd=" + query + "&";
            string queryEsc = System.Uri.EscapeUriString(query); 
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

        private void textDesc_TextChanged(object sender, EventArgs e)
        {
            if (!preventGenerateCode) EncodeData();
        }

        private void textMRN_TextChanged(object sender, EventArgs e)
        {
            if (!preventGenerateCode) EncodeData();
        }

        private void textLoc_TextChanged(object sender, EventArgs e)
        {
            if (!preventGenerateCode) EncodeData();
        }

        private void textDate_TextChanged(object sender, EventArgs e)
        {
            if (!preventGenerateCode) EncodeData();
        }

        private void textStudy_TextChanged(object sender, EventArgs e)
        {
            if (!preventGenerateCode) EncodeData();
        }

        private void checkBoxFollow_CheckedChanged(object sender, EventArgs e)
        {
            if (!preventGenerateCode) EncodeData();
        }

    }
}
