/**
  * Copyright (c) Analog Devices Inc, 2018 - 2020
  * All Rights Reserved.
  *
  * THIS SOFTWARE UTILIZES LIBRARIES DEVELOPED
  * AND MAINTAINED BY CYPRESS INC. THE LICENSE INCLUDED IN
  * THIS REPOSITORY DOES NOT EXTEND TO CYPRESS PROPERTY.
  *
  * Use of this file is governed by the license agreement
  * included in this repository.
  *
  * @file		ErrorLog.h
  * @date		12/6/2019
  * @author		A. Nolan (alex.nolan@analog.com)
  * @brief 		Implementation file for error logging capabilities
 **/

#include "ErrorLog.h"

/* Tell the compiler where to find the needed globals */
extern BoardState FX3State;
extern uint8_t FirmwareID[32];

/** Error log buffer */
uint8_t LogBuffer[FLASH_PAGE_SIZE];

/* Helper functions */
static void FindFirmwareVersion(uint8_t* buf);
static void WriteLogToFlash(ErrorMsg* msg);
static void WriteLogToDebug(ErrorMsg* msg);
static uint32_t GetNewLogAddress();
static uint32_t GetLogCount();

/**
  * @brief Logs a firmware error to flash memory for later examination
  *
  * @param File The file which produced the error
  *
  * @param Line The line in the source file which produced the error
  *
  * @param ErrorCode The error code which produced the error. Should be of type CyU3PReturnStatus_t.
  *
  * @return void
  *
  * When calling this function, the Line parameter should be generated by the C
  * pre-processor using the __LINE__ macro. Internally, this function sets the
  * unit boot time stamp (FX3 boot time), and FX3 firmware version before
  * logging the error to the debugger output (serial port) and flash.
 **/
void AdiLogError(FileIdentifier File, uint32_t Line, uint32_t ErrorCode)
{
	ErrorMsg error = {};

	/* Set the file code */
	error.File = File;

	/* Set the line */
	error.Line = Line;

	/* Set the error code */
	error.ErrorCode = ErrorCode;

	/* Set the boot time */
	error.BootTimeCode = FX3State.BootTime;

	/* Set the firmware version */
	FindFirmwareVersion(error.FirmwareVersion);

	/* Print to debug */
	WriteLogToDebug(&error);

	/* Store to flash */
	WriteLogToFlash(&error);
}

/**
  * @brief Sets the error log count value in flash
  *
  * @param count The new error log count value to write to flash
  *
  * @return void
 **/
void WriteErrorLogCount(uint32_t count)
{
	LogBuffer[0] = count & 0xFF;
	LogBuffer[1] = (count & 0xFF00) >> 8;
	LogBuffer[2] = (count & 0xFF0000) >> 16;
	LogBuffer[3] = (count & 0xFF000000) >> 24;
	AdiFlashWrite(LOG_COUNT_ADDR, 4, LogBuffer);
}

/**
  * @brief Parses the firmware version from FirmwareID to a user provided buffer
  *
  * @param outBuf The buffer to place the firmware version into
  *
  * @return void
 **/
static void FindFirmwareVersion(uint8_t* outBuf)
{
	uint32_t offset = 12;
	for(int i = 0; i < 12; i++)
	{
		outBuf[i] = FirmwareID[offset + i];
	}
}

/**
  * @brief Writes a log data structure to flash memory
  *
  * @param msg The error log object to write
  *
  * @return void
  *
  * This function first gets the address for the new buffer entry
  * from the flash error log ring buffer. It then clears the 32 bytes
  * which have been allocated in flash for the current error log. The
  * passed log is then copied byte-wise into the LogBuffer, array,
  * then written to flash. Finally, the LOG_COUNT variable in flash
  * is incremented.
 **/
static void WriteLogToFlash(ErrorMsg* msg)
{
	uint32_t logAddr, logCount;
	uint8_t* memPtr;

	/* Get the starting address of the next record based on the number of logs stored */
	logAddr = GetNewLogAddress(&logCount);

	/* Set log buffer to 0xFF*/
	for(int i = 0; i < 32; i++)
	{
		LogBuffer[i] = 0xFF;
	}

	/* Apply clear to flash flash */
	AdiFlashWrite(logAddr, 32, LogBuffer);

	/* Copy the error message to the Log Buffer */
	memPtr = (uint8_t *) msg;
	for(int i = 0; i < 32; i++)
	{
		LogBuffer[i] = memPtr[i];
	}

	/* Transfer log to flash */
	AdiFlashWrite(logAddr, 32, LogBuffer);

	/* Increment log count and store back to flash */
	logCount++;
	WriteErrorLogCount(logCount);
}

/**
  * @brief Prints an error log object to the debug console
  *
  * @param msg The error log object to print
  *
  * @return void
 **/
static void WriteLogToDebug(ErrorMsg* msg)
{
	CyU3PDebugPrint (4, "Error occurred on line %d of file %d. Error code: 0x%x\r\n", msg->Line, msg->File, msg->ErrorCode);
}

/**
  * @brief Gets address (in flash memory) for a new error log entry
  *
  * @param TotalLogCount Return by reference for the total number of error log entries stored in flash
  *
  * @return The flash address of the new error log entry
  *
  * This function implements the error log circular buffer logic
  * by mod-ing the count with the capacity (newer entries will overwrite
  * the oldest error log once the buffer is full).
 **/
static uint32_t GetNewLogAddress(uint32_t* TotalLogCount)
{
	/* Get the total lifetime log count from flash */
	uint32_t count = GetLogCount();

#ifdef VERBOSE_MODE
	CyU3PDebugPrint (4, "Error log count: 0x%x\r\n", count);
#endif

	/* Find location of "front" */
	uint32_t logFlashCount = count % LOG_CAPACITY;

	/* 32 bytes per log */
	uint32_t addr = logFlashCount * 32;

	/* Add offset */
	addr += LOG_BASE_ADDR;

	/* Return total count by reference */
	*TotalLogCount = count;

#ifdef VERBOSE_MODE
	CyU3PDebugPrint (4, "New Log Address: 0x%x\r\n", addr);
#endif

	/* Return address to write the new log to */
	return addr;
}

/**
  * @brief Gets the log count from flash
  *
  * @return The current logged error count (from LOG_COUNT)
 **/
static uint32_t GetLogCount()
{
	uint32_t count;

	/* Perform DMA flash read (4 bytes) */
	AdiFlashRead(LOG_COUNT_ADDR, 4, LogBuffer);

	/* Count values are stored little endian in flash */
	count = LogBuffer[0];
	count |= (LogBuffer[1] << 8);
	count |= (LogBuffer[2] << 16);
	count |= (LogBuffer[3] << 24);

	/* Handle un-initialized log */
	if(count == 0xFFFFFFFF)
	{
		count = 0;
	}
	return count;
}
