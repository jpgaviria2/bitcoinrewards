package com.bitcoinrewards.nfcdisplay.ndef;

import java.nio.charset.Charset;

/**
 * Builds NDEF URI record messages for lightning: URIs.
 * 
 * NDEF URI Record format:
 *   TNF: 0x01 (Well-Known)
 *   Type: "U" (0x55)
 *   Payload: 0x00 + full URI string
 *            0x00 = no URI prefix abbreviation (lightning: not in standard table)
 *
 * Output is wrapped in NDEF file format: 2-byte length prefix + NDEF record.
 */
public class NdefMessageBuilder {

    /**
     * Create an NDEF message containing a URI record.
     *
     * @param uri Full URI string, e.g. "lightning:lnurl1dp68gurn..."
     * @return Complete NDEF file bytes (2-byte length prefix + NDEF record)
     */
    public static byte[] createNdefUriMessage(String uri) {
        byte[] uriBytes = uri.getBytes(Charset.forName("UTF-8"));

        // URI record payload: [URI identifier code 0x00][URI string]
        byte[] payload = new byte[1 + uriBytes.length];
        payload[0] = 0x00; // No prefix abbreviation
        System.arraycopy(uriBytes, 0, payload, 1, uriBytes.length);

        byte[] type = {0x55}; // "U"

        // Build NDEF record header
        boolean isShortRecord = payload.length <= 255;
        int headerLen = isShortRecord ? 3 : 6;
        byte[] record = new byte[headerLen + type.length + payload.length];

        // Flags: MB=1, ME=1, CF=0, SR=?, IL=0, TNF=0x01
        record[0] = isShortRecord ? (byte) 0xD1 : (byte) 0xC1;
        record[1] = (byte) type.length;

        int idx;
        if (isShortRecord) {
            record[2] = (byte) payload.length;
            idx = 3;
        } else {
            int pl = payload.length;
            record[2] = (byte) ((pl >> 24) & 0xFF);
            record[3] = (byte) ((pl >> 16) & 0xFF);
            record[4] = (byte) ((pl >> 8) & 0xFF);
            record[5] = (byte) (pl & 0xFF);
            idx = 6;
        }

        System.arraycopy(type, 0, record, idx, type.length);
        idx += type.length;
        System.arraycopy(payload, 0, record, idx, payload.length);

        // Wrap in NDEF file format: 2-byte length prefix + record
        byte[] fullFile = new byte[2 + record.length];
        fullFile[0] = (byte) ((record.length >> 8) & 0xFF);
        fullFile[1] = (byte) (record.length & 0xFF);
        System.arraycopy(record, 0, fullFile, 2, record.length);

        return fullFile;
    }
}
