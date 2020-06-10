%Load wrapper DLL
NET.addAssembly('C:\Users\anolan3\Documents\iSensor-FX3-API\FX3ApiWrapper\bin\Debug\FX3ApiWrapper.dll');
%Check if the FX3 object is already instantiated (and connected)
if(exist('Dut','var') ~= 1)
    %Create FX3 wrapper, with ADIS1650x regmap
    Dut = FX3ApiWrapper.Wrapper('C:\Users\anolan3\Documents\iSensor-FX3-API\Resources',...
    'C:\Users\anolan3\Documents\iSensor-FX3-ExampleGui\src\ADIS1650x_Regmap.csv',...
    FX3ApiWrapper.SensorType.StandardImu);
end

%Blink user LED at 5Hz
Dut.UserLEDBlink(5.0);

%enable dr active
Dut.SetDrActive(true);

%Create reglist
regs = NET.createArray('System.String',3);
regs(1) = 'XACCL_UPR';
regs(2) = 'YACCL_UPR';
regs(3) = 'ZACCL_UPR';

%sample freq
Fs = 2000;
%sample period
T = 1/Fs;
% NFFT
L = 4096;
%fft frequency vector
f = Fs*(0:(L/2 - 1))/L;

%array to hold raw DUT data
rawData = [];

%data for each accel axis
x = zeros(1,L);
y = zeros(1,L);
z = zeros(1,L);

x_fft = [];
y_fft = [];
z_fft = [];

%index in raw data
i = 1;

while(true)
    rawData = int32(Dut.ReadSigned(regs, 1, L));
    i = 1;
    for n = 1:3:length(rawData)
        x(i) = rawData(n);
        y(i) = rawData(n + 1);
        z(i) = rawData(n + 2);
        i = i + 1;
    end
    
    %calc FFT
    x_fft = fft(x);
    y_fft = fft(y);
    z_fft = fft(z);
    
    %remove back half
    x_fft = x_fft(1:(L/2));
    y_fft = y_fft(1:(L/2));
    z_fft = z_fft(1:(L/2));
    
    %Scale to magnitude
    x_fft = abs(x_fft);
    y_fft = abs(y_fft);
    z_fft = abs(z_fft);
    
    %divide by fft length
    x_fft = x_fft / L;
    y_fft = y_fft / L;
    z_fft = z_fft / L;
    
    loglog(f, x_fft);
    hold on;
    loglog(f, y_fft);
    loglog(f, z_fft);
    hold off;
    
    xlabel('Frequency (in hertz)');
    title('ADIS1650x XL FFT');

    pause(1)
end