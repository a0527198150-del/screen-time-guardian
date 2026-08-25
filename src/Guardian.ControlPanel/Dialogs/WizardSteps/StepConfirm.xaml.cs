using System;
using System.Windows;
using System.Windows.Controls;

namespace ScreenTimeGuardian.ControlPanel.Dialogs.WizardSteps
{
    public partial class StepConfirm : UserControl
    {
        public event EventHandler? SaveRequested;

        public StepConfirm()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Set the confirmation message shown to the user.
        /// </summary>
        public void SetConfirmation(string message)
        {
            ConfirmationText.Text = message;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
