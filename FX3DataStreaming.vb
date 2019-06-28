﻿'File:          FX3DataStreaming.vb
'Author:        Alex Nolan (alex.nolan@analog.com), Juan Chong (juan.chong@analog.com)
'Date:          06/26/2019
'Description:   All streaming functions which run in the m_StreamingThread are implemented here

Imports System.Collections.Concurrent
Imports System.Threading
Imports AdisApi

Partial Class FX3Connection

#Region "Burst Stream Functions"

    ''' <summary>
    ''' Function to start a burst read using the BurstStreamManager
    ''' </summary>
    ''' <param name="numBuffers">The number of buffers to read in the stream operation</param>
    Private Sub StartBurstStream(ByVal numBuffers As UInteger)

        'Buffer to store command data
        Dim buf(8) As Byte

        'Wait for previous stream thread to exit, if any
        m_StreamThreadRunning = False
        While Not m_StreamThreadTerminated
        End While

        'Send number of buffers to read
        buf(0) = numBuffers And &HFF
        buf(1) = (numBuffers And &HFF00) >> 8
        buf(2) = (numBuffers And &HFF0000) >> 16
        buf(3) = (numBuffers And &HFF000000) >> 24

        'Send word to trigger burst
        buf(4) = (m_TriggerReg.Address And &HFF)
        buf(5) = (m_TriggerReg.Address And &HFF00) >> 8

        'Send number of words to capture
        buf(6) = (m_WordCount And &HFF)
        buf(7) = (m_WordCount And &HFF00) >> 8

        'Reinitialize the thread safe queue
        m_StreamData = New ConcurrentQueue(Of UShort())

        ConfigureControlEndpoint(USBCommands.ADI_STREAM_BURST_DATA, True)
        m_ActiveFX3.ControlEndPt.Value = 0 'DNC
        m_ActiveFX3.ControlEndPt.Index = 1 'Start stream

        'Send start stream command to the DUT
        If Not XferControlData(buf, 8, 2000) Then
            Throw New FX3CommunicationException("ERROR: Timeout occured while starting burst stream")
        End If

        'Reset number of frames read
        m_FramesRead = 0

        'Set the total number of frames to read
        m_TotalBuffersToRead = numBuffers

        'Set the thread control bool
        m_StreamThreadRunning = True

        'Spin up a BurstStreamManager thread
        m_StreamThread = New Thread(AddressOf BurstStreamManager)
        m_StreamThread.Start()

    End Sub

    ''' <summary>
    ''' Stops a burst stream by setting the stream state variables
    ''' </summary>
    Public Sub StopBurstStream()

        'Buffer to store command data
        Dim buf(3) As Byte

        'Configure the endpoint
        ConfigureControlEndpoint(USBCommands.ADI_STREAM_BURST_DATA, False)
        m_ActiveFX3.ControlEndPt.Value = 0
        m_ActiveFX3.ControlEndPt.Index = 0

        'Stop the stream manager thread
        m_StreamThreadRunning = False

        'Send command to the DUT to stop streaming data
        If Not XferControlData(buf, 4, 2000) Then
            Throw New FX3CommunicationException("ERROR: Timeout occured while stopping burst stream")
        End If

    End Sub

    ''' <summary>
    ''' This function reads burst stream data from the DUT over the streaming endpoint. It is intended to operate in its own thread, and should not be called directly.
    ''' </summary>
    Private Sub BurstStreamManager()

        'The length of one frame, in bytes
        Dim frameLength As Integer
        'The index in the current raw buffer
        Dim index As Integer
        'The index in the current frame
        Dim frameIndex As Integer = 0
        'Temporary value for converting two bytes to a UShort
        Dim shortValue As UShort
        'The USB transfer size (from the FX3)
        Dim transferSize As Integer
        'List used to construct frames out of the output buffer
        Dim frameBuilder As New List(Of UShort)
        'Bool to track the transfer status
        Dim transferStatus As Boolean
        'Int to track number of frames read
        Dim framesCounter As Integer

        'Validate the transfer size
        If m_ActiveFX3.bSuperSpeed Then
            transferSize = 1024
        ElseIf m_ActiveFX3.bHighSpeed Then
            transferSize = 512
        Else
            Throw New FX3GeneralException("ERROR: Streaming application requires USB 2.0 or 3.0 connection to function")
        End If

        'Buffer to hold data from the FX3
        Dim buf(transferSize - 1) As Byte

        'Set total frames (infinite if less than 1)
        If m_TotalBuffersToRead < 1 Then
            m_TotalBuffersToRead = Int32.MaxValue
        End If

        'Determine the frame length (in bytes) based on configured word count plus trigger word
        frameLength = (m_WordCount * 2) + 2

        'Set the stream thread running state variable
        m_StreamThreadRunning = True
        m_StreamThreadTerminated = False
        framesCounter = 0

        While m_StreamThreadRunning
            'Configured transfer size bytes from the FX3
            transferStatus = StreamingEndPt.XferData(buf, transferSize)
            'Parse bytes into frames and add to m_StreamData if transaction was successful
            If transferStatus Then
                For index = 0 To transferSize - 2 Step 2
                    'Append every two bytes into words
                    shortValue = buf(index)
                    shortValue = shortValue << 8
                    shortValue = shortValue + buf(index + 1)
                    frameBuilder.Add(shortValue)
                    frameIndex = frameIndex + 2
                    'Once the end of each frame is reached add it to the queue
                    If frameIndex >= frameLength Then
                        'Remove trigger word entry
                        frameBuilder.RemoveAt(0)
                        'Enqueue data into thread-safe queue
                        m_StreamData.Enqueue(frameBuilder.ToArray())
                        'Increment the shared frame counter
                        Interlocked.Increment(m_FramesRead)
                        'Increment the local frame counter
                        framesCounter = framesCounter + 1
                        'Reset frame builder list and counter
                        frameIndex = 0
                        frameBuilder.Clear()
                        'Exit if the total number of buffers has been read
                        If framesCounter >= m_TotalBuffersToRead Then
                            'Stop streaming
                            Exit While
                        End If
                    End If
                Next
            Else
                'Exit streaming mode if the transfer fails
                Exit While
            End If
        End While

        'Update m_StreamThreadRunning state variable
        m_StreamThreadRunning = False
        m_StreamThreadTerminated = True

    End Sub

#End Region

#Region "Generic Stream Functions"

    ''' <summary>
    ''' Stops a generic stream by setting the stream state variables
    ''' </summary>
    Public Sub StopGenericStream()

        'Buffer to hold command data
        Dim buf(3) As Byte

        'Configure the control endpoint
        ConfigureControlEndpoint(USBCommands.ADI_STREAM_GENERIC_DATA, False)
        m_ActiveFX3.ControlEndPt.Value = 0
        m_ActiveFX3.ControlEndPt.Index = 0

        'Send command to the DUT to stop streaming data
        If Not XferControlData(buf, 4, 2000) Then
            Throw New FX3CommunicationException("ERROR: Timeout occured when stopping a generic stream")
        End If

        'Stop the stream manager thread
        m_StreamThreadRunning = False

    End Sub

    ''' <summary>
    ''' Starts a generic data stream. This allows you to read/write a set of registers on the DUT, triggering off the data ready if needed.
    ''' The data read is placed in the threadsafe queue and can be retrieved with a call to GetBuffer. Each "buffer" is the result of
    ''' reading the addr list of registers numCaptures times. For example, if addr is set to [0, 2, 4] and numCaptures is set to 10, each
    ''' buffer will contain the 30 register values. The total number of register reads performed is numCaptures * numBuffers
    ''' </summary>
    ''' <param name="addr">The list of registers to </param>
    ''' <param name="numCaptures">The number of captures of the register list per data ready</param>
    ''' <param name="numBuffers">The total number of capture sequences to perform</param>
    Public Sub StartGenericStream(addr As IEnumerable(Of AddrDataPair), numCaptures As UInteger, numBuffers As UInteger)

        'Buffer to store control data
        Dim buf As New List(Of Byte)
        Dim BytesPerBuffer As Integer

        'Number of bytes per buffer
        BytesPerBuffer = (addr.Count() * numCaptures) * 2

        'Reset frame counter
        m_FramesRead = 0
        'Set the total number of frames to read
        m_TotalBuffersToRead = numBuffers

        'Add numBuffers
        buf.Add(numBuffers And &HFF)
        buf.Add((numBuffers And &HFF00) >> 8)
        buf.Add((numBuffers And &HFF0000) >> 16)
        buf.Add((numBuffers And &HFF000000) >> 24)

        'Add numCaptures
        buf.Add(numCaptures And &HFF)
        buf.Add((numCaptures And &HFF00) >> 8)
        buf.Add((numCaptures And &HFF0000) >> 16)
        buf.Add((numCaptures And &HFF000000) >> 24)

        'Add address list
        For Each item In addr
            If item.data Is Nothing Then
                buf.Add(item.addr And &HFF)
                buf.Add(&H0)
            Else
                buf.Add(item.addr And &HFF)
                buf.Add(item.data And &HFF)
            End If
        Next

        'Reinitialize the data queue
        m_StreamData = New ConcurrentQueue(Of UShort())

        'Configure the control endpoint
        ConfigureControlEndpoint(USBCommands.ADI_STREAM_GENERIC_DATA, True)

        'Configure settings to enable/disable streaming
        m_ActiveFX3.ControlEndPt.Value = 0
        m_ActiveFX3.ControlEndPt.Index = 1

        'Send start command to the FX3
        If Not XferControlData(buf.ToArray(), buf.Count, 5000) Then
            Throw New FX3CommunicationException("ERROR: Control Endpoint transfer timed out")
        End If

        'Start the Generic Stream Thread
        m_StreamThread = New Thread(AddressOf GenericStreamManager)
        m_StreamThread.Start(BytesPerBuffer)

    End Sub


    ''' <summary>
    ''' This function pulls generic stream data from the FX3 over a bulk endpoint (DataIn). It is intended to run in its own thread,
    ''' and should not be called by itself.
    ''' </summary>
    ''' <param name="BytesPerBuffer">Number of bytes per generic stream buffer</param>
    Private Sub GenericStreamManager(BytesPerBuffer As Integer)

        'Bool to track if transfer from FX3 board is successful
        Dim validTransfer As Boolean = True
        'Variable to track number of buffers read
        Dim numBuffersRead As Integer = 0
        'List to build output buffer in USHORT format
        Dim bufferBuilder As New List(Of UShort)
        'Int to track buffer indexrsfer sf
        Dim bufIndex As Integer = 0
        'Short value for flipping bytes
        Dim shortValue As UShort

        'Find transfer size and create data buffer
        Dim transferSize As Integer
        If m_ActiveFX3.bSuperSpeed Then
            transferSize = 1024
        ElseIf m_ActiveFX3.bHighSpeed Then
            transferSize = 512
        Else
            Throw New FX3GeneralException("ERROR: Streaming application requires USB 2.0 or 3.0 connection to function")
        End If

        'Buffer to hold data from the FX3
        Dim buf(transferSize - 1) As Byte

        'Set the thread state flags
        m_StreamThreadTerminated = False
        m_StreamThreadRunning = True

        While m_StreamThreadRunning
            'Read data from FX3
            validTransfer = StreamingEndPt.XferData(buf, transferSize)
            'Check that the data was read correctly
            If validTransfer Then
                'Build the output buffer
                For bufIndex = 0 To (transferSize - 2) Step 2
                    'Flip bytes
                    shortValue = buf(bufIndex)
                    shortValue = shortValue << 8
                    shortValue = shortValue + buf(bufIndex + 1)
                    bufferBuilder.Add(shortValue)
                    If bufferBuilder.Count() * 2 >= BytesPerBuffer Then
                        m_StreamData.Enqueue(bufferBuilder.ToArray())
                        Interlocked.Increment(m_FramesRead)
                        bufferBuilder.Clear()
                        numBuffersRead = numBuffersRead + 1
                        'Exit for loop if total integer number of buffers for the USB packet have been read
                        If (transferSize - bufIndex) < BytesPerBuffer Then
                            Exit For
                        End If
                        'Exit while loop if the total number of buffers has been read
                        If numBuffersRead >= m_TotalBuffersToRead Then
                            Exit While
                        End If
                    End If
                Next
            Else
                'Exit for a failed data transfer
                Exit While
            End If
        End While

        'Set thread state flags
        m_StreamThreadTerminated = True
        m_StreamThreadRunning = False

    End Sub

#End Region

#Region "Real-Time Stream Functions"

    ''' <summary>
    ''' This function starts real time streaming on the ADcmXLx021 (interface and FX3). Specifying pin exit is optional and must be 0 (disabled) or 1 (enabled)
    ''' </summary>
    Public Sub StartRealTimeStreaming(ByVal numFrames As UInteger)

        'Buffer to store command data
        Dim buf(4) As Byte

        'Wait for previous stream thread to exit, if any
        m_StreamThreadRunning = False
        While Not m_StreamThreadTerminated
        End While

        buf(0) = numFrames And &HFF
        buf(1) = (numFrames And &HFF00) >> 8
        buf(2) = (numFrames And &HFF0000) >> 16
        buf(3) = (numFrames And &HFF000000) >> 24
        buf(4) = (m_pinStart)

        'Reinitialize the thread safe queue
        m_StreamData = New ConcurrentQueue(Of UShort())

        'Configure the control endpoint
        ConfigureControlEndpoint(USBCommands.ADI_STREAM_REALTIME, True)
        m_ActiveFX3.ControlEndPt.Value = m_pinExit
        m_ActiveFX3.ControlEndPt.Index = 1

        'Send start stream command to the DUT
        If Not XferControlData(buf, 5, 2000) Then
            Throw New FX3CommunicationException("ERROR: Timeout occured while starting real time streaming")
        End If

        'Set the thread control bool
        m_StreamThreadRunning = True

        'Reset number of frames read
        m_FramesRead = 0
        m_numBadFrames = 0

        'Set the total number of frames to read
        m_TotalBuffersToRead = numFrames

        'Spin up a RealTimeStreamManager thread
        m_StreamThread = New Thread(AddressOf RealTimeStreamManager)
        m_StreamThread.Start()

    End Sub

    ''' <summary>
    ''' This function stops real time streaming on the ADcmXLx021 (interface and FX3)
    ''' </summary>
    Public Sub StopRealTimeStreaming()

        'Buffer to hold command data
        Dim buf(3) As Byte

        'Validate the current FX3 settings
        ValidateRealTimeStreamConfig()

        'Configure the control endpoint
        ConfigureControlEndpoint(USBCommands.ADI_STREAM_REALTIME, False)
        m_ActiveFX3.ControlEndPt.Value = m_pinExit
        m_ActiveFX3.ControlEndPt.Index = 0

        'Send command to the DUT to stop streaming data
        If Not XferControlData(buf, 4, 2000) Then
            Throw New FX3CommunicationException("ERROR: Timeout occured while stopping a real time stream")
        End If

        'Stop the stream manager thread
        m_StreamThreadRunning = False

    End Sub

    ''' <summary>
    ''' This function pulls real time data from the DUT over the streaming endpoint. It is intended to operate in its own thread, and should not be called directly
    ''' </summary>
    Private Sub RealTimeStreamManager()

        'The length of one frame, in bytes
        Dim frameLength As Integer
        'The index in the current 1KB buffer
        Dim index As Integer
        'The index in the current frame
        Dim frameIndex As Integer = 0
        'The total number of frames read so far
        Dim totalFrames As Integer = 0
        'Temporary value for converting two bytes to a UShort
        Dim shortValue As UShort
        'The USB transfer size (from the FX3)
        Dim transferSize As Integer
        'List used to construct frames out of the output buffer
        Dim frameBuilder As New List(Of UShort)
        'Bool to track the transfer status
        Dim TransferStatus As Boolean
        'Int to track number of frames read
        Dim framesCounter As Integer

        'Check endpoint speed
        If m_ActiveFX3.bSuperSpeed Then
            transferSize = 1024
        ElseIf m_ActiveFX3.bHighSpeed Then
            transferSize = 512
        Else
            Throw New FX3GeneralException("ERROR: Streaming application requires USB 2.0 or 3.0 connection to function")
        End If

        'Buffer to hold data from the FX3
        Dim buf(transferSize - 1) As Byte

        'Set total frames (infinite if less than 1)
        If m_TotalBuffersToRead < 1 Then
            m_TotalBuffersToRead = Int32.MaxValue
        End If

        'Determine the frame length based on DUTType
        If m_FX3_FX3SPIConfig.DUTType = DUTType.ADcmXL1021 Then
            'Single Axis
            frameLength = 64 * 1 + 16 + 8 '88
        ElseIf m_FX3_FX3SPIConfig.DUTType = DUTType.ADcmXL2021 Then
            'Two Axis
            frameLength = 64 * 2 + 16 + 8 '152
        Else
            'Three Axis (Default)
            frameLength = 64 * 3 + 8 '200
        End If

        'Set the stream thread running state variable
        m_StreamThreadRunning = True
        m_StreamThreadTerminated = False
        framesCounter = 0

        While m_StreamThreadRunning
            'Pull 1024 bytes from the DUT
            TransferStatus = StreamingEndPt.XferData(buf, transferSize)
            'Parse the 1024 bytes into frames and add to m_StreamData if transaction was successful
            If TransferStatus Then
                For index = 0 To transferSize - 2 Step 2
                    'Flip bytes
                    shortValue = buf(index)
                    shortValue = shortValue << 8
                    shortValue = shortValue + buf(index + 1)
                    frameBuilder.Add(shortValue)
                    frameIndex = frameIndex + 2
                    'Once the end of each frame is reached add it to the queue
                    If frameIndex >= frameLength Then
                        m_StreamData.Enqueue(frameBuilder.ToArray())
                        'Increment shared frame counter
                        Interlocked.Increment(m_FramesRead)
                        framesCounter = framesCounter + 1
                        'Reset frame builder list
                        frameIndex = 0
                        frameBuilder.Clear()
                        'Check that the total number of specified frames hasn't been read
                        If framesCounter >= m_TotalBuffersToRead Then
                            'Stop streaming
                            Exit While
                        End If
                    End If
                Next
            Else
                'Exit streaming mode if the transfer fails
                Exit While
            End If
        End While

        'Update m_StreamThreadRunning state variable
        m_StreamThreadRunning = False
        m_StreamThreadTerminated = True

    End Sub


    ''' <summary>
    ''' This function checks the CRC of each frame stored in the Stream Data Queue, and purges the bad ones
    ''' </summary>
    ''' <returns>The success of the data purge operation</returns>
    Public Function PurgeBadFrameData() As Boolean

        'Initialize variables
        Dim purgeSuccess As Boolean = True
        Dim frameDequeued As Boolean = False
        Dim tempQueue As New ConcurrentQueue(Of UShort())
        Dim frame() As UShort = Nothing

        'Only works for ADcmXLx021
        If Not (PartType = DUTType.ADcmXL1021 Or PartType = DUTType.ADcmXL2021 Or PartType = DUTType.ADcmXL3021) Then
            purgeSuccess = False
            Return purgeSuccess
        End If

        'Cannot run while streaming data
        If m_StreamThreadRunning Then
            purgeSuccess = False
            Return purgeSuccess
        End If

        'Pull data from queue
        m_numBadFrames = 0
        If purgeSuccess Then
            While Not m_StreamData.Count = 0
                'Dequeue the frame
                frameDequeued = False
                While Not frameDequeued And m_StreamData.Count > 0
                    frameDequeued = m_StreamData.TryDequeue(frame)
                End While
                'Check the CRC
                If CheckDUTCRC(frame) Then
                    tempQueue.Enqueue(frame)
                Else
                    m_numBadFrames = m_numBadFrames + 1
                End If
            End While
        End If

        'Set the output queue equal to the temp queue
        m_StreamData = tempQueue
        m_FramesRead = m_StreamData.Count

        Return purgeSuccess

    End Function

    ''' <summary>
    ''' This function validates the current SPI settings to ensure that they are compatible with the machine health
    ''' real time streaming mode. If the settings are not compatible, a FX3ConfigException is thrown.
    ''' </summary>
    Private Sub ValidateRealTimeStreamConfig()

    End Sub

#End Region

End Class