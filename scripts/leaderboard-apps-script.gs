/**
 * Poker Settle online leaderboard: paste this into the Google Sheet's Apps Script editor
 * (Extensions > Apps Script) and deploy it as a web app. See "Online leaderboard" in README.md.
 *
 *   POST  (the app)   {secret, updatedText, currency, rows[]}  ->  rewrites the "Leaderboard" sheet
 *   GET   (everyone)  the same /exec URL shows the leaderboard as a web page (read-only)
 *
 * The secret lives in Project Settings > Script properties as SECRET. Without it nobody can write.
 */

var SHEET_NAME = 'Leaderboard';
var HEADERS = ['Rank', 'Player', 'Net', 'Games', 'Win %', 'Average', 'Won', 'Lost', 'Best', 'Worst', 'Last played'];

function doPost(e) {
  var lock = LockService.getScriptLock();
  lock.waitLock(20000);
  try {
    var body = JSON.parse(e.postData.contents);
    var secret = (PropertiesService.getScriptProperties().getProperty('SECRET') || '').trim();
    if (!secret || body.secret !== secret) return reply({ ok: false, error: 'wrong secret' });
    writeSheet(body);
    return reply({ ok: true });
  } catch (err) {
    return reply({ ok: false, error: String(err) });
  } finally {
    lock.releaseLock();
  }
}

function writeSheet(body) {
  var book = SpreadsheetApp.getActiveSpreadsheet();
  var sheet = book.getSheetByName(SHEET_NAME) || book.insertSheet(SHEET_NAME, 0);
  sheet.clear();

  var rows = (body.rows || []).map(function (r) {
    return [r.rank, r.name, r.net, r.games, r.winRate, r.average, r.won, r.lost,
      r.biggestWin == null ? '' : r.biggestWin, r.biggestLoss == null ? '' : r.biggestLoss, r.lastPlayed || ''];
  });

  sheet.getRange(1, 1).setValue('Poker Settle leaderboard').setFontSize(14).setFontWeight('bold');
  sheet.getRange(2, 1).setValue('Updated ' + body.updatedText).setFontColor('#666666');
  sheet.getRange(3, 1, 1, HEADERS.length).setValues([HEADERS]).setFontWeight('bold').setBackground('#eeeeee');
  sheet.setFrozenRows(3);
  if (rows.length === 0) return;

  var data = sheet.getRange(4, 1, rows.length, HEADERS.length);
  data.setValues(rows);
  var money = '"' + body.currency + '"#,##0.00;-"' + body.currency + '"#,##0.00;"' + body.currency + '"0';
  [3, 6, 7, 8, 9, 10].forEach(function (col) { sheet.getRange(4, col, rows.length, 1).setNumberFormat(money); });
  sheet.getRange(4, 5, rows.length, 1).setNumberFormat('0%');
  sheet.getRange(4, 3, rows.length, 1).setFontColors(rows.map(function (r) {
    return [r[2] > 0 ? '#1b7f3b' : r[2] < 0 ? '#c62828' : '#000000'];
  })).setFontWeight('bold');
  sheet.autoResizeColumns(1, HEADERS.length);
}

/** The public page: reads the sheet, so it always matches it. */
function doGet() {
  var sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(SHEET_NAME);
  var updated = sheet ? sheet.getRange(2, 1).getDisplayValue() : 'Not published yet';
  var values = sheet && sheet.getLastRow() >= 4
    ? sheet.getRange(4, 1, sheet.getLastRow() - 3, HEADERS.length).getDisplayValues() : [];
  var nets = sheet && values.length ? sheet.getRange(4, 3, values.length, 1).getValues() : [];

  var cols = [0, 1, 2, 3, 4, 5]; // Rank, Player, Net, Games, Win %, Average
  var html = '<!doctype html><html><head><meta charset="utf-8"><title>Poker leaderboard</title><style>' +
    'body{font-family:system-ui,sans-serif;margin:0;padding:16px;background:#fafafa;color:#1a1a1a}' +
    'h1{font-size:22px;margin:0 0 4px}p{color:#666;margin:0 0 16px;font-size:14px}' +
    'table{border-collapse:collapse;width:100%;max-width:760px;background:#fff;font-size:15px}' +
    'th,td{padding:10px 8px;border-bottom:1px solid #eee;text-align:right;white-space:nowrap}' +
    'th:nth-child(2),td:nth-child(2){text-align:left;white-space:normal}th{background:#f2f2f2;font-size:13px}' +
    '.up{color:#1b7f3b;font-weight:600}.down{color:#c62828;font-weight:600}' +
    '@media(prefers-color-scheme:dark){body{background:#121212;color:#eee}table{background:#1e1e1e}' +
    'th{background:#2a2a2a}th,td{border-color:#333}.up{color:#5ad17f}.down{color:#ff7b7b}}' +
    '</style></head><body><h1>Poker leaderboard</h1><p>' + esc(updated) + '</p><table><tr>' +
    cols.map(function (c) { return '<th>' + HEADERS[c] + '</th>'; }).join('') + '</tr>';
  values.forEach(function (row, i) {
    var cls = nets[i][0] > 0 ? 'up' : nets[i][0] < 0 ? 'down' : '';
    html += '<tr>' + cols.map(function (c) {
      return '<td' + (c === 2 && cls ? ' class="' + cls + '"' : '') + '>' + esc(c === 0 ? '#' + row[c] : row[c]) + '</td>';
    }).join('') + '</tr>';
  });
  html += '</table></body></html>';

  return HtmlService.createHtmlOutput(html)
    .setTitle('Poker leaderboard')
    .addMetaTag('viewport', 'width=device-width, initial-scale=1');
}

function reply(obj) {
  return ContentService.createTextOutput(JSON.stringify(obj)).setMimeType(ContentService.MimeType.JSON);
}

function esc(s) {
  return String(s).replace(/[&<>"]/g, function (c) {
    return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c];
  });
}
