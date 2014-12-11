﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;
using System.IO.Ports;
using System.IO;

namespace CarSimulator
{
    public partial class MainForm : Form
    {
        private CommThread _commThread;
        private int _lastPortCount;
        private List<CommThread.ResponseEntry> _responseList;

        public MainForm()
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;
            InitializeComponent();

            _lastPortCount = -1;
            _responseList = new List<CommThread.ResponseEntry>();
            UpdateResponseFiles();
            UpdatePorts();
            _commThread = new CommThread();
            timerUpdate.Enabled = true;
            UpdateDisplay();
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            _commThread.StopThread();
        }

        private void UpdatePorts()
        {
            string[] ports = SerialPort.GetPortNames();
            if (ports == null)
            {
                ports = new string[0];
            }
            if (_lastPortCount == ports.Length) return;
            listPorts.BeginUpdate();
            listPorts.Items.Clear();
            int index = -1;
            foreach (string port in ports)
            {
                index = listPorts.Items.Add(port);
            }
            listPorts.SelectedIndex = index;
            listPorts.EndUpdate();

            buttonConnect.Enabled = listPorts.SelectedIndex >= 0;
            _lastPortCount = ports.Length;
        }

        private void UpdateResponseFiles()
        {
            string appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string[] files = Directory.GetFiles(appDir, "*.txt");
            listBoxResponseFiles.BeginUpdate();
            listBoxResponseFiles.Items.Clear();
            string selectItem = null;
            foreach (string file in files)
            {
                string baseFileName = Path.GetFileName(file);
                if (string.Compare(baseFileName, "Response.txt", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    selectItem = baseFileName;
                }
                listBoxResponseFiles.Items.Add(baseFileName);
            }
            if (selectItem != null)
            {
                listBoxResponseFiles.SelectedItem = selectItem;
            }
            listBoxResponseFiles.EndUpdate();
        }

        private bool ReadResponseFile(string fileName)
        {
            if (!File.Exists(fileName)) return false;

            try
            {
                _responseList.Clear();
                using (StreamReader streamReader = new StreamReader(fileName))
                {
                    string line;
                    while ((line = streamReader.ReadLine()) != null)
                    {
                        if (line.StartsWith(";")) continue;
                        if (line.Length < 2) continue;

                        string[] numberArray = line.Split(new char[] { ' ' });
                        bool responseData = false;
                        List<byte> listCompare = new List<byte>();
                        List<byte> listResponse = new List<byte>();

                        foreach (string number in numberArray)
                        {
                            if (number == ":")
                            {
                                responseData = true;
                            }
                            else
                            {
                                try
                                {
                                    int value = Convert.ToInt32(number, 16);
                                    if (responseData)
                                    {
                                        listResponse.Add((byte)value);
                                    }
                                    else
                                    {
                                        listCompare.Add((byte)value);
                                    }
                                }
                                catch
                                {
                                }
                            }
                        }
                        if (listCompare.Count > 0 && listResponse.Count > 0)
                        {
                            // find duplicates
                            bool addEntry = true;
                            foreach (CommThread.ResponseEntry responseEntry in _responseList)
                            {
                                if (listCompare.Count != responseEntry.Request.Length) continue;
                                bool equal = true;
                                for (int i = 0; i < listCompare.Count; i++)
                                {
                                    if (listCompare[i] != responseEntry.Request[i])
                                    {
                                        equal = false;
                                        break;
                                    }
                                }
                                if (equal)
                                {       // entry found
                                    if (responseEntry.Response.Length < listResponse.Count)
                                    {
                                        _responseList.Remove(responseEntry);
                                    }
                                    else
                                    {
                                        addEntry = false;
                                    }
                                    break;
                                }
                            }

                            if (addEntry)
                            {
                                _responseList.Add(new CommThread.ResponseEntry(listCompare.ToArray(), listResponse.ToArray()));
                            }
                        }
                    }
                }

                // split multi telegram responses
                foreach (CommThread.ResponseEntry responseEntry in _responseList)
                {
                    {
                        if (responseEntry.Request.Length < 4)
                        {
                            MessageBox.Show("Invalid response file request length!");
                        }
                        else
                        {
                            int telLength = responseEntry.Request[0] & 0x3F;
                            if (telLength == 0)
                            {   // with length byte
                                telLength = responseEntry.Request[3] + 5;
                            }
                            else
                            {
                                telLength += 4;
                            }
                            if (telLength != responseEntry.Request.Length)
                            {
                                MessageBox.Show("Invalid response file request!");
                            }
                        }
                    }

                    int telOffset = 0;
                    while ((telOffset + 1) < responseEntry.Response.Length)
                    {
                        if ((responseEntry.Response.Length - telOffset) < 4)
                        {
                            break;
                        }
                        int telLength = responseEntry.Response[telOffset + 0] & 0x3F;
                        if (telLength == 0)
                        {   // with length byte
                            telLength = responseEntry.Response[telOffset + 3] + 5;
                        }
                        else
                        {
                            telLength += 4;
                        }
                        if (telOffset + telLength > responseEntry.Response.Length)
                        {
                            break;
                        }
                        byte[] responseTel = new byte[telLength];
                        Array.Copy(responseEntry.Response, telOffset, responseTel, 0, telLength);
                        responseEntry.ResponseList.Add(responseTel);
                        telOffset += telLength;
                    }
                    if (telOffset != responseEntry.Response.Length)
                    {
                        MessageBox.Show("Invalid response file response!");
                    }
                }

            }
            catch
            {
                return false;
            }
            return true;
        }

        private void UpdateDisplay()
        {
            if (_commThread.ThreadRunning())
            {
                buttonConnect.Text = "Disconnect";
            }
            else
            {
                buttonConnect.Text = "Connect";
            }
            if (_commThread.ThreadRunning())
            {
                _commThread.Moving = checkBoxMoving.Checked;
                _commThread.VariableValues = checkBoxVariableValues.Checked;
                _commThread.IgnitionOk = checkBoxIgnitionOk.Checked;
            }
        }

        private void buttonConnect_Click(object sender, EventArgs e)
        {
            if (_commThread.ThreadRunning())
            {
                _commThread.StopThread();
            }
            else
            {
                if (listPorts.SelectedIndex < 0) return;
                string selectedPort = listPorts.SelectedItem.ToString();

                string appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string responseFile = (string)listBoxResponseFiles.SelectedItem;
                if (responseFile != null)
                {
                    if (!ReadResponseFile(Path.Combine(appDir, responseFile)))
                    {
                        MessageBox.Show("Reading response file failed!");
                    }
                }

                CommThread.ConceptType conceptType = CommThread.ConceptType.conceptBwmFast;
                if (radioButtonKwp2000S.Checked) conceptType = CommThread.ConceptType.conceptKwp2000S;
                if (radioButtonDs2.Checked) conceptType = CommThread.ConceptType.conceptDs2;
                _commThread.StartThread(selectedPort, conceptType, _responseList);
            }

            UpdateDisplay();
        }

        private void timerUpdate_Tick(object sender, EventArgs e)
        {
            UpdatePorts();
            UpdateDisplay();
        }

        private void checkBoxMoving_CheckedChanged(object sender, EventArgs e)
        {
            UpdateDisplay();
        }

        private void checkBoxIgnitionOk_CheckedChanged(object sender, EventArgs e)
        {
            UpdateDisplay();
        }
    }
}
