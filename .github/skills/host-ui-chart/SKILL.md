---
name: host-ui-chart
description: "Use when user asks about Host UI chart/realtime plotting features, including Y1/Y2 register selection, Start/Stop polling, Accumulate, Latest N window, axis range settings, HEX display, and loading CSV logs back to plot."
---

# Host UI Chart Skill

## Purpose
Provide a consistent workflow for operating and troubleshooting the Realtime chart area in Grididea BMS HT Tool.

## Use When
- User asks how to use the chart in Realtime tab.
- User asks how to select Y1/Y2 curves.
- User asks how to record and replay chart logs.
- User asks why chart does not move/update.
- User asks how HEX display affects chart values.

## Prerequisites
1. COM port, baud rate, and slave ID are set correctly.
2. Device is connected and responds to Modbus polling.
3. At least one register is selected in Y1 or Y2 list.

## Standard Operation Flow
1. Click Connect.
2. Select at least one register from Y1 Registers (left) and/or Y2 Registers (right).
3. Set Period(s) for polling.
4. Click Start.
5. Observe curve updates in Realtime Plot Area.
6. Click Stop when done.

## Chart Controls
- Accumulate:
  - On: keep historical points.
  - Off: show rolling/latest data behavior.
- Latest N:
  - Controls visible point window size.
  - Reduce N for smoother UI on slower PCs.
- Y1 / Y2 Axis:
  - Auto: automatically scale axis.
  - Manual: uncheck Auto and set Min/Max.
- HEX Display:
  - Changes displayed value format between decimal and hex.
  - Does not change underlying register raw data.

## Log And Replay
1. Set CSV path in top toolbar.
2. Start polling to generate log rows.
3. Stop polling.
4. Click Load Log Plot to re-import CSV and redraw history.

CSV format expectation:
- Timestamp,Name,Address,Value,Unit

## Quick Troubleshooting
- No curve shown:
  - Check Connect state.
  - Ensure at least one Y1 or Y2 register is checked.
  - Confirm polling is running (Start pressed).
- Flat line/unexpected values:
  - Verify selected register address and unit scaling.
  - Toggle HEX Display to confirm interpretation issue is only formatting.
- Replay failed:
  - Verify CSV path and file existence.
  - Verify CSV columns match expected format.
- Axis looks wrong:
  - Re-enable Auto on Y1/Y2, then test again.

## Response Template
When helping users, answer in this order:
1. Current state check (connected? polling? register selected?).
2. Minimal operation steps to reproduce expected chart behavior.
3. Control-specific tuning (Latest N, Auto axis, Accumulate).
4. Log/replay verification if historical data is needed.
5. Next actionable check if still failing.
