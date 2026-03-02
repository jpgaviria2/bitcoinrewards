package com.bitcoinrewards.nfcdisplay.ndef;

/**
 * Constants for NDEF Type 4 Tag emulation.
 * Adapted from Numo — standard NDEF values, no changes needed.
 */
public class NdefConstants {

    // Command Headers
    public static final byte[] NDEF_SELECT_FILE_HEADER = {0x00, (byte) 0xA4, 0x00, 0x0C};

    // SELECT AID command (NDEF Type 4 Tag AID: D2760000850101)
    public static final byte[] NDEF_SELECT_AID = {
            0x00, (byte) 0xA4, 0x04, 0x00,
            0x07,
            (byte) 0xD2, 0x76, 0x00, 0x00, (byte) 0x85, 0x01, 0x01,
            0x00
    };

    // Capability Container file
    public static final byte[] CC_FILE_ID = {(byte) 0xE1, 0x03};
    public static final byte[] CC_FILE = {
            0x00, 0x0F,               // CCLEN = 15
            0x20,                     // Mapping version 2.0
            0x00, 0x3B,               // MLe (max read)
            0x00, 0x34,               // MLc (max write)
            0x04,                     // T (NDEF File Control TLV)
            0x06,                     // L
            (byte) 0xE1, 0x04,        // File ID
            (byte) 0x70, (byte) 0xFF, // Max size
            0x00,                     // Read access: unrestricted
            0x00                      // Write access: unrestricted
    };

    // NDEF file
    public static final byte[] NDEF_FILE_ID = {(byte) 0xE1, 0x04};

    // READ BINARY header
    public static final byte[] NDEF_READ_BINARY_HEADER = {0x00, (byte) 0xB0};

    // Responses
    public static final byte[] NDEF_RESPONSE_OK = {(byte) 0x90, 0x00};
    public static final byte[] NDEF_RESPONSE_ERROR = {(byte) 0x6A, (byte) 0x82};
}
