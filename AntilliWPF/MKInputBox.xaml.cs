﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using System.Windows.Forms;

namespace Antilli
{
    /// <summary>
    /// Interaction logic for MKInputBox.xaml
    /// </summary>
    public partial class MKInputBox : ObservableWindow
    {
        private bool _showCancelButton;

        public string InputValue
        {
            get { return ValueText.Text; }
        }

        public bool ShowCancelButton
        {
            get { return _showCancelButton; }
            set
            {
                _showCancelButton = value;

                btnCancel.Visibility = (_showCancelButton) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public MKInputBox(string title, string prompt, string defaultValue = "")
        {
            InitializeComponent();

            Title = title;
            PromptText.Content = prompt;

            Loaded += (o, e) => {
                ValueText.Text = defaultValue;
                ValueText.Focus();

                if (defaultValue.Length > 0)
                    ValueText.Select(0, defaultValue.Length);
            };

            btnOk.Click += (o, e) => {
                DialogResult = true;
                Close();
            };

            btnCancel.Click += (o, e) => {
                DialogResult = false;
                Close();
            };
        }
    }
}
