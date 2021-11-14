using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpectrumPlotter
{
    public partial class PolyfitForm : Form
    {
        public delegate void PolyfitAddedEvent(double sensor, double reference);

        public static PolyfitForm CurrentForm = null;

        public event PolyfitAddedEvent PolyfitAdded;

        double _Sensor = 0;
        double _Reference = 0;

        public double Sensor
        {
            get => _Sensor;
            set => txtSensor.Text = value.ToString();
        }
        public double Reference
        {
            get => _Reference;
            set => txtReference.Text = value.ToString();
        }

        public PolyfitForm()
        {
            InitializeComponent();

            PolyfitAdded += (double sensor, double reference) => { };

            CurrentForm = this;
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            if (!double.TryParse(txtSensor.Text, out _Sensor))
            {
                return;
            }
            if (!double.TryParse(txtReference.Text, out _Reference))
            {
                return;
            }
            txtSensor.Text = "";
            txtReference.Text = "";
            txtSensor.Focus();
            PolyfitAdded(_Sensor, _Reference);
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            CurrentForm = null;
            Close();
        }
    }
}
