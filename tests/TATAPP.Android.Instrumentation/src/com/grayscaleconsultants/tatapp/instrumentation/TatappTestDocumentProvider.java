package com.grayscaleconsultants.tatapp.instrumentation;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.Bundle;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;
import android.util.Log;

import java.io.ByteArrayOutputStream;
import java.io.FileNotFoundException;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

public final class TatappTestDocumentProvider extends ContentProvider {
    static final String AUTHORITY = "com.grayscaleconsultants.tatapp.instrumentation.documents";
    static final Uri SUCCESS_URI = Uri.parse("content://" + AUTHORITY + "/success.png");
    static final Uri FAILURE_URI = Uri.parse("content://" + AUTHORITY + "/failure.png");
    private static final int MAX_DOCUMENT_BYTES = 16 * 1024 * 1024;
    private static final String TAG = "TATAPP-TestProvider";

    private final Map<String, byte[]> inputs = new ConcurrentHashMap<>();
    private volatile byte[] successfulOutput = new byte[0];

    static Uri inputUri(String name) {
        return fixtureUri("input", name);
    }

    static Uri setupInputUri(String name) {
        return fixtureUri("setup-input", name);
    }

    private static Uri fixtureUri(String operation, String name) {
        if (name == null || name.isEmpty() || !name.equals(Uri.encode(name)))
            throw new IllegalArgumentException("Input fixture name must be a nonempty path segment.");
        return new Uri.Builder()
                .scheme("content")
                .authority(AUTHORITY)
                .appendPath(operation)
                .appendPath(name)
                .build();
    }

    @Override public boolean onCreate() { return true; }

    @Override public String getType(Uri uri) { return "image/png"; }

    @Override public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        if (FAILURE_URI.equals(uri)) throw new FileNotFoundException("Synthetic provider write failure.");
        if (isInputUri(uri)) {
            if (!"r".equals(mode)) throw new FileNotFoundException("Input fixtures are read-only.");
            byte[] data = inputs.get(fixtureName(uri));
            if (data == null || data.length == 0)
                throw new FileNotFoundException("Input fixture is missing or incomplete.");
            return readPipe(uri, data);
        }
        if (isSetupInputUri(uri)) {
            if (mode == null || !mode.contains("w"))
                throw new FileNotFoundException("Fixture setup endpoints are write-only.");
            String name = fixtureName(uri);
            if (inputs.putIfAbsent(name, new byte[0]) != null)
                throw new FileNotFoundException("Input fixture is already sealed for reading.");
            try {
                return writePipe(uri, data -> inputs.put(name, data), () -> inputs.remove(name));
            } catch (FileNotFoundException error) {
                inputs.remove(name);
                throw error;
            }
        }
        if (!SUCCESS_URI.equals(uri) || mode == null)
            throw new FileNotFoundException("Unsupported test document URI or mode.");
        if (mode.contains("w")) {
            successfulOutput = new byte[0];
            return writePipe(uri, data -> successfulOutput = data,
                    () -> successfulOutput = new byte[0]);
        }
        if (!"r".equals(mode) || successfulOutput.length == 0)
            throw new FileNotFoundException("Successful SAF output is not ready.");
        return readPipe(uri, successfulOutput);
    }

    @Override public Cursor query(Uri uri, String[] projection, String selection,
                                  String[] selectionArgs, String sortOrder) {
        String[] columns = projection == null || projection.length == 0
                ? new String[] { OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE }
                : projection;
        String displayName;
        long size;
        if (isInputUri(uri) || isSetupInputUri(uri)) {
            displayName = fixtureName(uri);
            byte[] data = inputs.get(displayName);
            size = data == null ? 0 : data.length;
        } else if (SUCCESS_URI.equals(uri) || FAILURE_URI.equals(uri)) {
            displayName = "tatapp-instrumentation-export.png";
            size = successfulOutput.length;
        } else {
            throw new IllegalArgumentException("Unsupported test document URI.");
        }
        MatrixCursor cursor = new MatrixCursor(columns);
        MatrixCursor.RowBuilder row = cursor.newRow();
        for (String column : columns) {
            if (OpenableColumns.DISPLAY_NAME.equals(column)) row.add(displayName);
            else if (OpenableColumns.SIZE.equals(column)) row.add(size);
            else row.add(null);
        }
        return cursor;
    }

    private ParcelFileDescriptor readPipe(Uri uri, byte[] data) throws FileNotFoundException {
        return openPipeHelper(uri, "image/png", null, data,
                (output, ignoredUri, ignoredMime, ignoredOptions, bytes) -> {
                    try (OutputStream stream = new ParcelFileDescriptor.AutoCloseOutputStream(output)) {
                        stream.write(bytes);
                    } catch (IOException error) {
                        Log.e(TAG, "Could not write an in-memory test document pipe.", error);
                    }
                });
    }

    private static ParcelFileDescriptor writePipe(Uri uri, ByteSink completed, Runnable failed)
            throws FileNotFoundException {
        final ParcelFileDescriptor[] pipe;
        try {
            pipe = ParcelFileDescriptor.createPipe();
        } catch (IOException error) {
            FileNotFoundException failure = new FileNotFoundException("Could not create test document pipe.");
            failure.initCause(error);
            throw failure;
        }
        Thread reader = new Thread(() -> {
            try (InputStream input = new ParcelFileDescriptor.AutoCloseInputStream(pipe[0]);
                 ByteArrayOutputStream buffer = new ByteArrayOutputStream()) {
                byte[] block = new byte[16 * 1024];
                int count;
                while ((count = input.read(block)) >= 0) {
                    if (count == 0) continue;
                    if (buffer.size() > MAX_DOCUMENT_BYTES - count)
                        throw new IOException("Test document exceeds the in-memory limit.");
                    buffer.write(block, 0, count);
                }
                completed.accept(buffer.toByteArray());
            } catch (IOException error) {
                failed.run();
                Log.e(TAG, "Could not read an in-memory test document pipe for " + uri + ".", error);
            }
        }, "tatapp-test-provider-writer");
        reader.start();
        return pipe[1];
    }

    private static boolean isInputUri(Uri uri) {
        return isFixtureUri(uri, "input");
    }

    private static boolean isSetupInputUri(Uri uri) {
        return isFixtureUri(uri, "setup-input");
    }

    private static boolean isFixtureUri(Uri uri, String operation) {
        return uri != null && AUTHORITY.equals(uri.getAuthority()) &&
                uri.getPathSegments().size() == 2 &&
                operation.equals(uri.getPathSegments().get(0));
    }

    private static String fixtureName(Uri uri) {
        if (!isInputUri(uri) && !isSetupInputUri(uri))
            throw new IllegalArgumentException("Unsupported input fixture URI.");
        String name = uri.getPathSegments().get(1);
        if (name.isEmpty() || !name.equals(Uri.encode(name)))
            throw new IllegalArgumentException("Unsafe input fixture name.");
        return name;
    }

    @Override public Uri insert(Uri uri, ContentValues values) {
        throw new UnsupportedOperationException("The test provider does not support insert.");
    }

    @Override public int delete(Uri uri, String selection, String[] selectionArgs) {
        if (isInputUri(uri) || isSetupInputUri(uri))
            return inputs.remove(fixtureName(uri)) == null ? 0 : 1;
        if (SUCCESS_URI.equals(uri)) {
            successfulOutput = new byte[0];
            return 1;
        }
        throw new IllegalArgumentException("Unsupported test document URI.");
    }

    @Override public int update(Uri uri, ContentValues values, String selection, String[] selectionArgs) {
        throw new UnsupportedOperationException("The test provider does not support update.");
    }

    private interface ByteSink { void accept(byte[] data); }
}
