-- 1. SensorDataLog 테이블: Agent로부터 1초마다 수신되는 성능 데이터를 저장합니다.
--    (Disk, Network 컬럼 추가)
CREATE TABLE SensorDataLog (
    LogID INT IDENTITY(1,1) PRIMARY KEY,    -- 자동 증가 기본 키
    Timestamp DATETIME NOT NULL DEFAULT GETDATE(), -- 로그 기록 시간 (기본값: 현재 시간)
    DeviceID NVARCHAR(50) NOT NULL,            -- Agent 식별자 (예: "Agent-01")
    CpuUsage FLOAT,                            -- CPU 사용률 (%)
    RamUsagePercent FLOAT,                     -- RAM 사용률 (%)
    DiskUsagePercent FLOAT,                    -- Disk 사용률 (%)
    NetworkSent FLOAT,                         -- Network Sent (Bytes/sec)
    NetworkReceived FLOAT,                     -- Network Received (Bytes/sec)
    VirtualTemp FLOAT                          -- 가상 온도 (섭씨)
);
GO

-- 2. AuditLog 테이블: Dashboard에서 실행된 원격 제어 명령을 기록합니다.
CREATE TABLE AuditLog (
    AuditID INT IDENTITY(1,1) PRIMARY KEY,   -- 자동 증가 기본 키
    Timestamp DATETIME NOT NULL DEFAULT GETDATE(), -- 명령 실행 시간
    UserID NVARCHAR(50),                       -- 명령을 내린 사용자 (MVP에서는 "Host"로 고정)
    TargetDeviceID NVARCHAR(50) NOT NULL,      -- 명령 대상 Agent ID
    Command NVARCHAR(50),                      -- 실행된 명령 (예: "START", "STOP")
    CommandValue NVARCHAR(100)                 -- 명령 관련 값 (예: "SET_SP"의 "85.0")
);
GO

-- 3. 성능 향상을 위한 인덱스 생성
CREATE INDEX IX_SensorDataLog_Timestamp ON SensorDataLog(Timestamp);
CREATE INDEX IX_SensorDataLog_DeviceID ON SensorDataLog(DeviceID);
CREATE INDEX IX_AuditLog_Timestamp ON AuditLog(Timestamp);
GO