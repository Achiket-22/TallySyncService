# TallySyncService - Quick Start Guide

## Overview

Your TallySyncService is fully configured and ready to sync data from Tally Prime to your backend server running on `localhost:3000`.

**Status**: ✅ All systems operational
- Backend server: Running at `http://localhost:3000/data`
- Service: Built and ready
- Integration: Verified with test payload

---

## Quick Start (3 Steps)

### Step 1: Configure the Service

```bash
cd /home/achiket/Documents/work/onlyoncloud/TallySyncService

# Interactive setup (select company and tables)
dotnet run -- --setup

# This will ask you:
# 1. Tally URL (default: http://localhost:9000)
# 2. Backend URL (default: http://localhost:3000)
# 3. Which Tally company to sync
# 4. Which tables to sync (LEDGER, STOCKITEM, VOUCHER, etc.)
```

### Step 2: Test the Sync

```bash
# Test with your configured company/tables
dotnet run -- --test-sync

# This will:
# ✓ Connect to Tally
# ✓ Fetch data from selected tables
# ✓ Convert XML to JSON
# ✓ Send sample to backend
# ✓ Show record count and sample data
```

**Output example**:
```
Testing Tally connection...
✓ Tally connection successful

Testing backend connection...
✓ Backend connection successful

Testing sync for table: LEDGER
  Fetching data from Tally...
  ✓ Fetched 5234 bytes from Tally
  
  ✓ Converted to 42 JSON records
  
  Sending data to backend...
  ✓ Backend accepted data
```

### Step 3: Run the Service

```bash
# Run as continuous service
dotnet run

# Service will:
# 1. Fetch all data from selected tables (initial sync)
# 2. Send data to backend in chunks
# 3. Run incremental syncs every 15 minutes
# 4. Continue until you stop it (Ctrl+C)
```

**Watch backend logs**:
```bash
# In another terminal
cd /home/achiket/Documents/work/onlyoncloud/TallySyncService/temp
tail -f nohup.out  # or check console where server.js is running
```

**Expected output** (backend logs):
```
POST /data
Received 100 records for LEDGER
Chunk 1/5, Mode: FULL
INSERT: 001-ledger-1
INSERT: 001-ledger-2
...
```

---

## Common Commands

### Check Service Status
```bash
dotnet run -- --status
```

Shows:
- Whether service is configured
- Selected tables
- Last sync times
- Total records synced

### Test Specific Table
```bash
dotnet run -- --test-sync
```

Fetches and validates data conversion for first selected table.

### List Tally Companies
```bash
dotnet run -- --test-companies
```

Shows companies available in Tally for selection.

### Authentication (if required)
```bash
# If RequireAuthentication is enabled
dotnet run -- --login

# Provides OTP and saves JWT token for future syncs
```

---

## Data Flow

```
Tally Prime (localhost:9000)
        ↓
[Fetch XML data]
        ↓
TallySyncService
        ↓
[Convert XML → JSON]
        ↓
[Detect changes (hash-based)]
        ↓
[Split into chunks of 100 records]
        ↓
Backend Server (localhost:3000/data)
        ↓
[Your processing logic]
```

---

## Configuration

Located at: `appsettings.json`

```json
{
  "TallySync": {
    "TallyUrl": "http://localhost:9000",
    "BackendUrl": "http://localhost:3000",
    "BackendSyncEndpoint": "/data",
    "SyncIntervalMinutes": 15,            // How often to sync
    "ChunkSize": 100,                     // Records per API call
    "InitialSyncDaysBack": 365,           // Initial lookback period
    "RequireAuthentication": true         // OTP-based auth
  }
}
```

**Adjust as needed**:
- **SyncIntervalMinutes**: 5-60 (higher = less frequent syncing)
- **ChunkSize**: 50-500 (higher = fewer API calls but larger payloads)
- **TallyUrl**: Change if Tally is on different machine
- **BackendUrl**: Change if backend moves to different port/host

---

## Backend Integration

### Your Backend Receives

**HTTP POST** to `http://localhost:3000/data`

**Headers**:
```
Content-Type: application/json
Authorization: Bearer <jwt_token>  (if authentication enabled)
```

**Body** (example):
```json
{
  "tableName": "LEDGER",
  "companyName": "Sample Company",
  "records": [
    {
      "id": "001-ledger-123",
      "operation": "INSERT",          // or UPDATE, DELETE
      "hash": "sha256hash...",
      "modifiedDate": "2025-01-22T10:30:00Z",
      "data": {
        "NAME": "Opening Balance",
        "GUID": "xyz789",
        "PARENT": "Assets",
        ...
      }
    },
    // ... up to 100 records per chunk
  ],
  "timestamp": "2025-01-22T10:30:00Z",
  "sourceIdentifier": "WORKSTATION-01",
  "totalRecords": 500,
  "chunkNumber": 1,
  "totalChunks": 5,
  "syncMode": "FULL"                  // or INCREMENTAL
}
```

### Your Backend Should Respond

```json
{
  "success": true,
  "processed": 100,
  "message": "Chunk accepted"
}
```

**Status codes**:
- `200`: Success
- `400`: Bad request (validation error)
- `401`: Unauthorized (bad token)
- `500`: Server error (will retry)

---

## Monitoring

### Check Backend is Receiving Data

```bash
# Health check
curl http://localhost:3000/health

# Expected:
# {"status":"ok","timestamp":"2025-01-22T..."}
```

### Monitor Service Logs

```bash
# Run service with visible logs
dotnet run

# Or save to file
dotnet run > sync.log 2>&1

# Watch logs
tail -f sync.log
```

### Key Log Markers

```
[INFO] Starting INITIAL SYNC for table: LEDGER
[INFO] Fetched 150 records
[INFO] Detected changes: 5 inserts, 3 updates
[INFO] Sending 8 records in 1 chunks
[INFO] Sync completed for table: LEDGER
```

---

## Troubleshooting

### Backend Not Receiving Data

1. **Check backend is running**:
   ```bash
   curl http://localhost:3000/health
   ```

2. **Check service configured**:
   ```bash
   dotnet run -- --status
   ```

3. **Test manually**:
   ```bash
   curl -X POST http://localhost:3000/data \
     -H "Content-Type: application/json" \
     -d '{"tableName":"TEST","records":[]}'
   ```

### Tally Not Accessible

1. **Verify Tally is running**:
   ```bash
   curl http://localhost:9000
   ```

2. **Check appsettings.json** for correct TallyUrl

3. **Test with sample data** without Tally:
   ```bash
   dotnet run -- --test-sync
   # Should work even if Tally unavailable initially
   ```

### Service Won't Start

1. **Check .NET is installed**:
   ```bash
   dotnet --version
   ```

2. **Rebuild**:
   ```bash
   dotnet clean
   dotnet build
   ```

3. **Check logs for errors**:
   ```bash
   dotnet run 2>&1 | head -50
   ```

---

## Advanced Options

### Disable Authentication (for testing)

Edit `appsettings.json`:
```json
"RequireAuthentication": false
```

Then restart service:
```bash
dotnet run
```

### Adjust Sync Frequency

Edit `appsettings.json`:
```json
"SyncIntervalMinutes": 5  // Sync every 5 minutes instead of 15
```

### Change Chunk Size

Edit `appsettings.json`:
```json
"ChunkSize": 250  // Larger chunks, fewer API calls
```

### Run as Background Service

**Linux (systemd)**:
```bash
# Copy service file
sudo cp tallysync.service /etc/systemd/system/

# Enable and start
sudo systemctl enable tallysync
sudo systemctl start tallysync

# Monitor
sudo journalctl -u tallysync -f
```

---

## Testing Scenarios

### Test 1: Initial Full Sync
```bash
# Setup
dotnet run -- --setup

# Test
dotnet run -- --test-sync

# Run
dotnet run
# Wait 5-10 seconds, then Ctrl+C
```

**Expected**: 
- All records from Tally sent to backend
- Records marked as "INSERT"
- Backend shows chunk count

### Test 2: Incremental Sync
```bash
# After initial sync is done:

# Modify 1 record in Tally (change a ledger name)
# Wait for next sync cycle (15 minutes by default)
# Or reduce interval: "SyncIntervalMinutes": 1

# Check logs for:
# - "Detected changes: 0 inserts, 1 update"
# - Only 1 record sent to backend
# - Record marked as "UPDATE"
```

### Test 3: Error Recovery
```bash
# Run service
dotnet run

# During sync, stop backend:
# cd ./temp && kill server (or Ctrl+C)

# Observe service logs:
# - "Backend connection failed"
# - Retries with exponential backoff
# - Completes when backend restarts
```

---

## Performance Tips

### For Large Datasets

1. **Increase chunk size**:
   ```json
   "ChunkSize": 500
   ```

2. **Increase timeouts**:
   ```json
   "TallyTimeoutSeconds": 120,
   "BackendTimeoutSeconds": 180
   ```

3. **Adjust sync frequency**:
   ```json
   "SyncIntervalMinutes": 30
   ```

### For Frequent Changes

1. **Decrease sync interval**:
   ```json
   "SyncIntervalMinutes": 5
   ```

2. **Smaller chunks**:
   ```json
   "ChunkSize": 50
   ```

---

## Support & Documentation

For detailed information, see:
- **CONFIGURATION_CHECKLIST.md** - Setup requirements
- **TESTING_GUIDE.md** - Test scenarios and troubleshooting
- **INCREMENTAL_SYNC_TESTING.md** - Change detection details
- **CHUNKED_DELIVERY_TESTING.md** - Backend integration details
- **INTEGRATION_TEST_REPORT.md** - Verification results

---

## Summary

Your TallySyncService is ready to:
1. ✅ Connect to Tally Prime
2. ✅ Fetch data from configured tables
3. ✅ Convert XML to JSON
4. ✅ Detect changes (initial sync → incremental syncs)
5. ✅ Send chunked data to your backend at `localhost:3000/data`
6. ✅ Handle errors and retries

**Next action**: Run `dotnet run -- --setup` to configure your company and tables!

