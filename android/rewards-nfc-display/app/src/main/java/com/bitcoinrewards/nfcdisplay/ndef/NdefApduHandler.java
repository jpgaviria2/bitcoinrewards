package com.bitcoinrewards.nfcdisplay.ndef;

import java.util.Arrays;

/**
 * Handles SELECT FILE and READ BINARY APDUs for NDEF Type 4 Tag emulation.
 * Simplified from Numo — always serves NDEF message when set (no write-mode gate).
 */
public class NdefApduHandler {
    private final NdefStateManager stateManager;

    public NdefApduHandler(NdefStateManager stateManager) {
        this.stateManager = stateManager;
    }

    public byte[] handleSelectFile(byte[] apdu) {
        byte[] fileId = Arrays.copyOfRange(apdu, 5, 7);

        if (Arrays.equals(fileId, NdefConstants.CC_FILE_ID)) {
            stateManager.setSelectedFile(NdefConstants.CC_FILE);
            return NdefConstants.NDEF_RESPONSE_OK;
        }

        if (Arrays.equals(fileId, NdefConstants.NDEF_FILE_ID)) {
            String message = stateManager.getMessageToSend();
            if (!message.isEmpty()) {
                byte[] ndefMessage = NdefMessageBuilder.createNdefUriMessage(message);
                stateManager.setSelectedFile(ndefMessage);
                return NdefConstants.NDEF_RESPONSE_OK;
            }
            return NdefConstants.NDEF_RESPONSE_ERROR;
        }

        return NdefConstants.NDEF_RESPONSE_ERROR;
    }

    public byte[] handleReadBinary(byte[] apdu) {
        byte[] selectedFile = stateManager.getSelectedFile();
        if (selectedFile == null) {
            return NdefConstants.NDEF_RESPONSE_ERROR;
        }

        int offset = 0;
        int length = selectedFile.length;

        if (apdu.length >= 4) {
            offset = ((apdu[2] & 0xFF) << 8) | (apdu[3] & 0xFF);
        }
        if (apdu.length >= 5) {
            length = (apdu[4] & 0xFF);
            if (length == 0) length = 256;
        }

        // Clamp to available data (don't error, just return what we have)
        if (offset >= selectedFile.length) {
            // Return empty with OK status
            return NdefConstants.NDEF_RESPONSE_OK;
        }
        if (offset + length > selectedFile.length) {
            length = selectedFile.length - offset;
        }

        byte[] data = Arrays.copyOfRange(selectedFile, offset, offset + length);
        byte[] response = new byte[data.length + 2];
        System.arraycopy(data, 0, response, 0, data.length);
        System.arraycopy(NdefConstants.NDEF_RESPONSE_OK, 0, response, data.length, 2);

        return response;
    }
}
