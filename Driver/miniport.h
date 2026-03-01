#pragma once

#include <portcls.h>
#include <ksdebug.h>

// Shared memory globals (defined in adapter.cpp)
extern PVOID   g_SharedBuffer;
extern PKEVENT g_DataReadyEvent;

// Factory function for creating the miniport
NTSTATUS CreateSoundBoxMiniport(PUNKNOWN* OutMiniport);

// Maximum number of miniports
#define MAX_MINIPORTS 1
