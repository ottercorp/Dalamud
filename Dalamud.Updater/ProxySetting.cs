using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Dalamud.Updater
{
    public partial class ProxySetting : Form
    {
        public string proxyHost ="127.0.0.1";
        public string proxyPort ="1080";
        public bool useProxy =false;

        public ProxySetting(bool useProxy,string proxyHost,string proxyPort)
        {
            InitializeComponent();
            radioButton1.Checked = !useProxy;
            radioButton2.Checked = useProxy;
            textHost.Text = proxyHost; 
            textPort.Text = proxyPort;
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {

        }

        private void radioButton_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton1.Checked)
            {
                textHost.Enabled = false;
                textPort.Enabled = false;
            }
            else
            {
                textHost.Enabled = true;
                textPort.Enabled = true;
            }
        }

        private void buttonOK_Click(object sender, EventArgs e)
        {
            this.proxyHost = textHost.Text;
            this.proxyPort = textPort.Text;
            this.useProxy = radioButton2.Checked;
            this.DialogResult = DialogResult.OK;
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
        }
    }
}
