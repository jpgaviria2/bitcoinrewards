package com.bitcoinrewards.nfcdisplay.ndef;

import java.util.Arrays;

/**
 * Routes APDU commands for read-only NDEF Type 4 Tag emulation.
 * No UPDATE BINARY support — this is a read-only tag.
 */
public class NdefProcessor {
    private final NdefStateManager stateManager;
    private final NdefApduHandler apduHandler;

    public NdefProcessor() {
        this.stateManager = new NdefStateManager();
        this.apduHandler = new NdefApduHandler(stateManager);
    }

    public void setMessageToSend(String message) {
        stateManager.setMessageToSend(message);
    }

    public byte[] processCommandApdu(byte[] commandApdu) {
        if (commandApdu == null || commandApdu.length < 2) {
            return NdefConstants.NDEF_RESPONSE_ERROR;
        }

        // SELECT AID — match the AID bytes flexibly (iOS may omit trailing Le byte)
        if (isSelectAidCommand(commandApdu)) {
            return NdefConstants.NDEF_RESPONSE_OK;
        }

        // SELECT FILE
        if (commandApdu.length >= 7 &&
            Arrays.equals(Arrays.copyOfRange(commandApdu, 0, 4), NdefConstants.NDEF_SELECT_FILE_HEADER)) {
            return apduHandler.handleSelectFile(commandApdu);
        }

        // READ BINARY (Numo uses >= 2, not >= 5)
        if (commandApdu.length >= 2 &&
            Arrays.equals(Arrays.copyOfRange(commandApdu, 0, 2), NdefConstants.NDEF_READ_BINARY_HEADER)) {
            return apduHandler.handleReadBinary(commandApdu);
        }

        return NdefConstants.NDEF_RESPONSE_ERROR;
    }

    /**
     * Check if command is a SELECT AID for NDEF Type 4.
     * Flexible match — checks AID bytes but allows different Le byte.
     * iOS and Android may send slightly different SELECT AID commands.
     */
    private boolean isSelectAidCommand(byte[] cmd) {
        if (cmd.length < 12) return false;
        // Check CLA=00, INS=A4, P1=04, P2=00, Lc=07
        if (cmd[0] != 0x00 || cmd[1] != (byte) 0xA4 || cmd[2] != 0x04 || cmd[3] != 0x00 || cmd[4] != 0x07) {
            return false;
        }
        // Check AID: D2760000850101
        byte[] expectedAid = {(byte) 0xD2, 0x76, 0x00, 0x00, (byte) 0x85, 0x01, 0x01};
        return Arrays.equals(Arrays.copyOfRange(cmd, 5, 12), expectedAid);
    }
}
