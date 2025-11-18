using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;

namespace DanawaRClient.ViewModels
{
    public class MainViewModel: INotifyPropertyChanged
    {
        // 1) 인터페이스에서 요구하는 이벤트
        public event PropertyChangedEventHandler PropertyChanged;

        // 2) 변경 알림 보내는 헬퍼 메서드 하나 만들어두면 편함
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // 3) 실제로 바인딩할 프로퍼티
        private double _diskTotalGaugeValue = 70;  // ← 여기서 70으로 하드코딩

        public double DiskTotalGaugeValue
        {
            get => _diskTotalGaugeValue;
            set
            {
                if (_diskTotalGaugeValue == value) return;
                _diskTotalGaugeValue = value;
                OnPropertyChanged(nameof(DiskTotalGaugeValue));  // ← 값 바뀔 때 UI에 알림
            }
        }
    }
}
