package com.bitcoinrewards.nfcdisplay.ndef;

/**
 * Utility methods for NDEF processing.
 * Copied from Numo as-is.
 */
public class NdefUtils {

    public static String bytesToHex(byte[] bytes) {
        StringBuilder sb = new StringBuilder();
        for (byte b : bytes) {
            sb.append(String.format("%02X", b));
        }
        return sb.toString();
    }
}
