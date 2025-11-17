using System;
using System.ComponentModel;
using System.Windows.Controls;
using LiveCharts;

namespace DanawaRClient.Views.UserControls
{
    public partial class LineChart : UserControl, INotifyPropertyChanged
    {
        private ChartValues<double> _chartValues;
        private const int MaxDataPoints = 60;

        public ChartValues<double> ChartValues
        {
            get { return _chartValues; }
            set
            {
                _chartValues = value;
                OnPropertyChanged(nameof(ChartValues));
            }
        }

        public LineChart()
        {
            InitializeComponent();

            // 초기 데이터 (60개의 0 값)
            ChartValues = new ChartValues<double>();
            for (int i = 0; i < MaxDataPoints; i++)
            {
                ChartValues.Add(0);
            }

            DataContext = this;
        }

        public void AddValue(double value)
        {
            // 새 값 추가
            ChartValues.Add(value);

            // 60개 초과시 가장 오래된 값 제거
            if (ChartValues.Count > MaxDataPoints)
            {
                ChartValues.RemoveAt(0);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}