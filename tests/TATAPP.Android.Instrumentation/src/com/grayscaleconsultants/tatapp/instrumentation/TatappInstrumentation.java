package com.grayscaleconsultants.tatapp.instrumentation;

import android.app.Activity;
import android.app.Application;
import android.app.Instrumentation;
import android.content.Intent;
import android.content.pm.ActivityInfo;
import android.database.Cursor;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Rect;
import android.graphics.drawable.BitmapDrawable;
import android.net.Uri;
import android.os.Bundle;
import android.os.SystemClock;
import android.provider.OpenableColumns;
import android.util.TypedValue;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.accessibility.AccessibilityNodeInfo;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.ImageView;
import android.widget.RadioButton;
import android.widget.ScrollView;
import android.widget.SeekBar;
import android.widget.Spinner;
import android.widget.Switch;
import android.widget.TextView;

import java.io.InputStream;
import java.io.OutputStream;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

public final class TatappInstrumentation extends Instrumentation {
    private static final String TARGET_PACKAGE = "com.grayscaleconsultants.tatapp";
    // ComponentCallbacks2.TRIM_MEMORY_RUNNING_CRITICAL is deprecated in API 35,
    // but level 15 remains the compatibility callback value on minSdk 26.
    private static final int LEGACY_TRIM_MEMORY_RUNNING_LOW = 10;
    private static final int LEGACY_TRIM_MEMORY_RUNNING_CRITICAL = 15;
    private final List<String> report = new ArrayList<>();
    private int passed;
    private int failed;
    private int skipped;
    private Activity activity;
    private ActivityTracker tracker;
    private final List<Uri> temporaryInputs = new ArrayList<>();
    private int stageCount;
    private int expectedRegionCount;

    @Override public void onCreate(Bundle arguments) {
        super.onCreate(arguments);
        String count = arguments == null ? null : arguments.getString("expectedRegionCount");
        try {
            expectedRegionCount = count == null ? 0 : Integer.parseInt(count);
        } catch (NumberFormatException ignored) {
            expectedRegionCount = 0;
        }
        start();
    }

    @Override public void onStart() {
        try {
            runSuite();
        } catch (Throwable error) {
            fail("runner", stack(error));
        } finally {
            cleanup();
            report.add(String.format(Locale.ROOT, "RESULT: %d passed, %d failed, %d skipped", passed, failed, skipped));
            Bundle result = new Bundle();
            result.putString(REPORT_KEY_STREAMRESULT, "\n" + String.join("\n", report) + "\n");
            result.putInt("passed", passed);
            result.putInt("failed", failed);
            result.putInt("skipped", skipped);
            finish(failed == 0 ? Activity.RESULT_OK : Activity.RESULT_CANCELED, result);
        }
    }

    private void runSuite() throws Exception {
        require(expectedRegionCount > 0,
                "Pass expectedRegionCount derived from the shared BodyRegionCatalog.");
        tracker = new ActivityTracker(TARGET_PACKAGE);
        Application application = (Application) getTargetContext().getApplicationContext();
        application.registerActivityLifecycleCallbacks(tracker);
        Intent launch = getTargetContext().getPackageManager().getLaunchIntentForPackage(TARGET_PACKAGE);
        require(launch != null, "Install TATAPP before running instrumentation.");
        launch.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
        activity = startActivitySync(launch);
        require(activity != null, "The launch activity did not start.");
        waitForIdleSync();

        test("launch exposes native names, headings, state, and polite status", this::testLaunchSemantics);
        test("initial focus order follows the primary workflow", this::testInitialFocusOrder);
        test("visible start actions meet the 48dp minimum", this::testTouchTargets);
        test("photo picker result loads a real PNG fixture", this::loadFixtureThroughPicker);
        test("workspace exposes body-picker alternative and defaults", this::testBodyAlternativeAndDefaults);
        test("non-default anatomy controls and model taps stay synchronized", this::testAnatomySynchronization);
        test("stage slider exposes Previous and Next accessibility actions", this::testStageAccessibilityActions);
        test("custom stage actions use application-namespace resource IDs", this::testStageActionIdNamespace);
        test("Previous and Next navigate deterministic settled stages", this::testPreviousNext);
        test("rapid stage movement publishes only the final settled stage", this::testRapidStageMovement);
        test("settled stage status is a single polite announcement source", this::testSettledStageStatusSource);
        test("low memory during stage debounce preserves displayed labels and export", this::testStageLowMemoryRace);
        test("workspace actions meet the 48dp minimum", this::testTouchTargets);
        test("SAF saves the exact visible flat stage", this::testSafSaveSuccess);
        test("SAF write failure preserves the visible workspace", this::testSafSaveFailure);
        test("rapid image replacement cannot publish the stale source", this::testRapidImageReplacement);
        test("corrupt input reports an error and preserves the workspace", this::testCorruptInputRecovery);
        test("reduced-motion control is directly operable and stateful", this::testReducedMotion);
        test("Offline AI cannot falsely report ready", this::testOfflineAiFence);
        test("picker cancellation preserves the loaded design", this::testPickerCancellation);
        test("camera cancellation preserves the loaded design", this::testCameraCancellation);
        test("BLACK WIDOW intent occurs only after deliberate activation", this::testBlackWidowActivation);
        test("critical-memory callbacks preserve a usable workspace", this::testLowMemoryRecovery);
        test("Back and Resume preserve the workspace", this::testBackAndResume);
        test("activity recreation preserves non-default anatomy and stage state", this::testRecreation);
        test("2x text in a compact viewport remains scrollable with 48dp actions", this::testLargeTextCompactLayout);
        test("orientation change recreates or is explicitly unsupported", this::testOrientationChange);

        skip("loaded-model descriptions and heartbeat lifetime",
                "Needs the external checksum-verified model artifacts installed on an eligible test target; the ordinary instrumentation run does not install them.");
        skip("human TalkBack speech quality and Explore by Touch",
                "Requires a person listening to TalkBack; node semantics are automated above.");
    }

    private void testLaunchSemantics() {
        View root = root();
        require(TARGET_PACKAGE.equals(activity.getPackageName()), "Expected target package is active.");
        TextView heading = findExact(root, "TATAPP");
        require(heading != null && heading.isAccessibilityHeading(), "TATAPP is an accessibility heading.");
        requireButton(root, "Take photo", true);
        requireButton(root, "Select photo", true);
        requireButton(root, "SAVE current look", false);
        requireButton(root, "BLACK WIDOW TATTOO", true);
        TextView status = findContaining(root, "Ready. Take or select a photo");
        require(status != null && status.getAccessibilityLiveRegion() == View.ACCESSIBILITY_LIVE_REGION_POLITE,
                "Application status is a polite live region.");
    }

    private void testInitialFocusOrder() {
        List<View> views = descendants(root());
        int take = indexOfText(views, "Take photo");
        int select = indexOfText(views, "Select photo");
        int link = indexOfText(views, "BLACK WIDOW TATTOO");
        require(take >= 0 && select > take && link > select,
                "Primary controls are ordered Take, Select, BLACK WIDOW.");
    }

    private void loadFixtureThroughPicker() throws Exception {
        Uri fixture = createPngFixture();
        Intent data = readableInputResult(fixture);
        dispatchActivityResult(1001, Activity.RESULT_OK, data);
        require(waitUntil(() -> findContaining(root(), "instrumentation-source.png") != null, 10_000),
                "The fixture was decoded and shown in workspace metadata.");
        TextView firstStage = findContaining(root(), "Stage 1 of ");
        require(firstStage != null, "Workspace exposes the repository-defined stage total.");
        Matcher count = Pattern.compile("Stage 1 of ([0-9]+): Original image")
                .matcher(firstStage.getText().toString());
        require(count.find(), "The first stage has a semantic index, total, and name.");
        stageCount = Integer.parseInt(count.group(1));
        require(stageCount > 1, "The stage catalog contains multiple significant stages.");
        require(findContaining(root(), stageLabel(1, "Original image")) != null,
                "Workspace starts at the original semantic stage.");
        requireButton(root(), "SAVE current look", true);
    }

    private void testBodyAlternativeAndDefaults() {
        Spinner region = null;
        for (View view : descendants(root())) {
            if (view instanceof Spinner && "Body region".contentEquals(view.getContentDescription())) {
                region = (Spinner) view;
                break;
            }
        }
        require(region != null, "A native body-region Spinner exists independently of the model.");
        require(region.getCount() == expectedRegionCount,
                "Body-region picker count matches the shared repository catalog.");
        Set<String> uniqueRegions = new HashSet<>();
        for (int index = 0; index < region.getCount(); index++) {
            String item = String.valueOf(region.getItemAtPosition(index)).trim();
            require(!item.isEmpty(), "Every body-region option has a readable label.");
            require(uniqueRegions.add(item), "Body-region labels are unique: " + item);
        }
        String selectedRegion = region.getSelectedItem().toString().toLowerCase(Locale.ROOT);
        require(selectedRegion.contains("left upper arm") && selectedRegion.contains("outer surface"),
                "Default accessible region is left outer upper arm.");
        TextView male = findExact(root(), "Male");
        require(male instanceof RadioButton && ((RadioButton) male).isChecked(), "Male is selected by default.");
        require(findContaining(root(), "163 centimeters") != null, "Default height is 163 centimeters.");
        require(findContaining(root(), "Light brown to medium tan") != null,
                "Default complexion is light brown to medium tan.");
        SeekBar skinTone = null;
        for (View view : descendants(root())) {
            CharSequence description = view.getContentDescription();
            if (view instanceof SeekBar && description != null &&
                    description.toString().startsWith("Skin tone and complexion")) {
                skinTone = (SeekBar) view;
                break;
            }
        }
        require(skinTone != null && skinTone.getStateDescription() != null &&
                        skinTone.getStateDescription().toString().toLowerCase(Locale.ROOT)
                                .contains("light brown to medium tan"),
                "Skin-tone slider exposes its selected complexion as semantic state.");
        List<View> ordered = descendants(root());
        int headingIndex = indexOfText(ordered, "Anatomical visualization");
        int previewIndex = -1;
        for (int index = 0; index < ordered.size(); index++) {
            CharSequence description = ordered.get(index).getContentDescription();
            if (description != null && description.toString().startsWith("Interactive male anatomical preview")) {
                previewIndex = index;
                break;
            }
        }
        require(headingIndex >= 0 && previewIndex > headingIndex,
                "Anatomical heading precedes its interactive visual preview in traversal order.");
    }

    private void testAnatomySynchronization() throws Exception {
        Spinner region = findSpinner("Body region");
        RadioButton female = requireRadioButton("Female");
        SeekBar bodySize = findSeekBar("Body size");
        SeekBar skinTone = findSeekBar("Skin tone and complexion");
        View anatomy = findByContentPrefix("Interactive male anatomical preview");
        require(region != null && bodySize != null && skinTone != null && anatomy != null,
                "Anatomy picker, sliders, and rendered model are present.");

        int rightCalf = findSpinnerItemContaining(region, "Right calf");
        require(rightCalf >= 0, "The shared region catalog exposes Right calf.");
        runOnMainSync(() -> {
            female.performClick();
            region.setSelection(rightCalf);
            setSeekBarProgress(bodySize, 40);
            setSeekBarProgress(skinTone, 78);
        });
        require(waitUntil(() -> {
            CharSequence description = anatomy.getContentDescription();
            return female.isChecked() && region.getSelectedItemPosition() == rightCalf &&
                    description != null && description.toString().toLowerCase(Locale.ROOT).contains("female") &&
                    description.toString().toLowerCase(Locale.ROOT).contains("right calf") &&
                    description.toString().contains("180 centimeters") &&
                    description.toString().toLowerCase(Locale.ROOT).contains("medium brown");
        }, 3_000), "Picker, sex, size, and complexion update the rendered-model semantic state. " +
                anatomyDiagnostics(region, bodySize, skinTone, anatomy));

        runOnMainSync(() -> setSeekBarProgress(skinTone, 25));
        require(waitUntil(() -> skinTone.getProgress() == 25, 1_000),
                "A lighter non-default complexion settles.");
        Bitmap lighter = captureView(anatomy);
        runOnMainSync(() -> setSeekBarProgress(skinTone, 78));
        require(waitUntil(() -> skinTone.getProgress() == 78 &&
                        anatomy.getContentDescription().toString().toLowerCase(Locale.ROOT)
                                .contains("medium brown"), 3_000),
                "The selected complexion returns to medium brown.");
        Bitmap darker = captureView(anatomy);
        try {
            require(!bitmapsEqual(lighter, darker),
                    "Changing complexion changes rendered model pixels, not only semantic text.");
        } finally {
            lighter.recycle();
            darker.recycle();
        }

        int beforeTap = region.getSelectedItemPosition();
        if (isTouchExplorationEnabled()) {
            skip("visual model tap synchronizes the standard picker",
                    "Touch exploration is enabled, so the model correctly defers to the accessible picker.");
        } else {
            require(selectDifferentRegionByModelTouch(anatomy, region, beforeTap),
                    "A direct model-surface tap selects a different standard picker option.");
            assertSelectedRegionMatchesAnatomy(region, anatomy);
        }

        runOnMainSync(() -> region.setSelection(rightCalf));
        require(waitUntil(() -> region.getSelectedItemPosition() == rightCalf &&
                anatomy.getContentDescription().toString().toLowerCase(Locale.ROOT).contains("right calf"), 3_000),
                "The standard picker updates the model after a visual-model selection.");
    }

    private void testStageAccessibilityActions() throws Exception {
        SeekBar stage = null;
        for (View view : descendants(root())) {
            CharSequence description = view.getContentDescription();
            if (view instanceof SeekBar && description != null && description.toString().startsWith("Visual development stage")) {
                stage = (SeekBar) view;
                break;
            }
        }
        require(stage != null, "Visual-development stage is a native SeekBar.");
        AccessibilityNodeInfo node = stage.createAccessibilityNodeInfo();
        {
            Integer previous = null;
            Integer next = null;
            for (AccessibilityNodeInfo.AccessibilityAction action : node.getActionList()) {
                String label = action.getLabel() == null ? "" : action.getLabel().toString();
                if ("Previous stage".equalsIgnoreCase(label)) previous = action.getId();
                if ("Next stage".equalsIgnoreCase(label)) next = action.getId();
            }
            require(previous == null, "Stage slider omits the no-op Previous action at the first stage.");
            require(next != null, "Stage slider exposes Next stage action.");
            require(node.getRangeInfo() != null, "Stage slider exposes range semantics.");

            SeekBar target = stage;
            int nextAction = next;
            runOnMainSync(() -> require(target.performAccessibilityAction(nextAction, null),
                    "Next stage accessibility action rejected activation."));
            require(waitUntil(() -> findContaining(root(), stagePrefix(2)) != null, 8_000),
                    "Next custom action settles on stage 2.");
            require(waitUntil(() -> findContaining(root(), readyStagePrefix(2)) != null, 8_000),
                    "Next custom action reaches ready state.");

            AccessibilityNodeInfo advancedNode = target.createAccessibilityNodeInfo();
            Integer previousAction = null;
            for (AccessibilityNodeInfo.AccessibilityAction action : advancedNode.getActionList()) {
                String label = action.getLabel() == null ? "" : action.getLabel().toString();
                if ("Previous stage".equalsIgnoreCase(label)) previousAction = action.getId();
            }
            require(previousAction != null, "Stage slider exposes Previous stage after advancing.");
            int previousActionId = previousAction;
            runOnMainSync(() -> require(target.performAccessibilityAction(previousActionId, null),
                    "Previous stage accessibility action rejected activation."));
            require(waitUntil(() -> findContaining(root(), stageLabel(1, "Original image")) != null, 8_000),
                    "Previous custom action returns to the original stage.");
        }
    }

    private void testStageActionIdNamespace() {
        SeekBar stage = null;
        for (View view : descendants(root())) {
            CharSequence description = view.getContentDescription();
            if (view instanceof SeekBar && description != null && description.toString().startsWith("Visual development stage")) {
                stage = (SeekBar) view;
                break;
            }
        }
        require(stage != null, "Visual-development stage is present.");
        AccessibilityNodeInfo node = stage.createAccessibilityNodeInfo();
        for (AccessibilityNodeInfo.AccessibilityAction action : node.getActionList()) {
            String label = action.getLabel() == null ? "" : action.getLabel().toString();
            if (!"Previous stage".equalsIgnoreCase(label) && !"Next stage".equalsIgnoreCase(label)) continue;
            require((action.getId() & 0xff000000) != 0x01000000,
                    label + " incorrectly uses the reserved android.R framework resource namespace: 0x" +
                            Integer.toHexString(action.getId()));
        }
    }

    private void testPreviousNext() throws Exception {
        String initial = findContaining(root(), stageLabel(1, "Original image")).getText().toString();
        click("Next Stage");
        require(waitUntil(() -> findContaining(root(), stagePrefix(2)) != null, 8_000),
                "Next Stage settles on stage 2.");
        require(waitUntil(() -> findContaining(root(), readyStagePrefix(2)) != null, 8_000),
                "Settled stage reports ready.");
        click("Previous Stage");
        require(waitUntil(() -> {
            TextView view = findContaining(root(), stagePrefix(1));
            return view != null && initial.contentEquals(view.getText());
        }, 8_000), "Previous Stage returns to the original stage.");
    }

    private void testRapidStageMovement() throws Exception {
        SeekBar stage = findSeekBar("Visual development stage");
        require(stage != null, "Visual-development stage slider is present.");
        runOnMainSync(() -> {
            setSeekBarProgress(stage, 7);
            setSeekBarProgress(stage, 100);
            setSeekBarProgress(stage, 21);
            setSeekBarProgress(stage, 51);
        });
        require(waitUntil(() -> exactTextExists(stageLabel(8, "Fine outline")) &&
                exactTextExists("Value 51 percent") && exactTextExists(readyStagePrefix(8) + " Fine outline."),
                10_000), "Only the final rapid-slider selection reaches the settled ready state. " +
                stageDiagnostics());
        require(findContaining(root(), "Ready. Stage 16 of") == null,
                "A stale render did not replace the final rapid-slider selection.");
    }

    private void testSettledStageStatusSource() {
        TextView status = findByContentPrefixAsText("Application status. Ready. Stage 8 of ");
        require(status != null, "The settled stage is exposed by the application status view. " +
                stageDiagnostics());
        String exact = readyStagePrefix(8) + " Fine outline.";
        require(exact.contentEquals(status.getText()),
                "Settled stage status has the exact semantic stage text. " + stageDiagnostics());
        require(("Application status. " + exact).contentEquals(status.getContentDescription()),
                "The status accessibility description exactly mirrors the settled stage. " + stageDiagnostics());
        require(status.getAccessibilityLiveRegion() == View.ACCESSIBILITY_LIVE_REGION_POLITE,
                "The settled stage source is a polite live region. " + stageDiagnostics());
        require(countExactText(root(), exact) == 1,
                "Exactly one visible status source exposes the settled ready announcement. " +
                        stageDiagnostics());
    }

    private void testSafSaveSuccess() throws Exception {
        ImageView preview = findImagePreview();
        require(preview != null && preview.getDrawable() instanceof BitmapDrawable,
                "A rendered preview bitmap is visible before save.");
        Bitmap expected = ((BitmapDrawable) preview.getDrawable()).getBitmap().copy(Bitmap.Config.ARGB_8888, false);
        try {
            assertSafExportMatches(expected, "Fine outline", "fine-outline");
        } finally {
            expected.recycle();
        }
    }

    private void testStageLowMemoryRace() throws Exception {
        SeekBar stage = findSeekBar("Visual development stage");
        ImageView preview = findImagePreview();
        require(stage != null && preview != null && preview.getDrawable() instanceof BitmapDrawable,
                "A settled stage and rendered preview exist before the memory-pressure race.");
        require(stage.getProgress() == 51 && exactTextExists(stageLabel(8, "Fine outline")),
                "The race starts from displayed Fine outline. " + stageDiagnostics());
        Bitmap expected = ((BitmapDrawable) preview.getDrawable()).getBitmap().copy(Bitmap.Config.ARGB_8888, false);
        try {
            runOnMainSync(() -> {
                setSeekBarProgress(stage, 21);
                activity.onTrimMemory(LEGACY_TRIM_MEMORY_RUNNING_LOW);
            });
            String expectedStatus = "Stage rendering stopped because Android reported memory pressure. " +
                    "Showing " + stageLabel(8, "Fine outline") + ".";
            require(waitUntil(() -> stage.getProgress() == 51 &&
                            exactTextExists(stageLabel(8, "Fine outline")) &&
                            exactTextExists("Value 51 percent") &&
                            exactTextExists(expectedStatus), 3_000),
                    "RUNNING_LOW restores slider, heading, value, and status to the retained frame. " +
                            stageDiagnostics());
            Thread.sleep(250);
            require(stage.getProgress() == 51 && exactTextExists(stageLabel(8, "Fine outline")) &&
                            exactTextExists("Value 51 percent") && exactTextExists(expectedStatus) &&
                            String.valueOf(preview.getContentDescription()).contains(stageLabel(8, "Fine outline")),
                    "The canceled stage cannot publish stale labels or preview state after debounce. " +
                            stageDiagnostics());
            assertSafExportMatches(expected, "Fine outline", "fine-outline");
        } finally {
            expected.recycle();
        }
    }

    private void assertSafExportMatches(Bitmap expected, String stageName, String titleToken) throws Exception {
        getContext().getContentResolver().delete(TatappTestDocumentProvider.SUCCESS_URI, null, null);

        Intent resultData = new Intent().setData(TatappTestDocumentProvider.SUCCESS_URI)
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
        RecordingActivityMonitor monitor = new RecordingActivityMonitor(Intent.ACTION_CREATE_DOCUMENT,
                new ActivityResult(Activity.RESULT_OK, resultData));
        addMonitor(monitor);
        try {
            click("SAVE current look");
            require(waitUntil(() -> documentSize(TatappTestDocumentProvider.SUCCESS_URI) > 0 &&
                    exactTextExists("Saved " + stageName + " as PNG image."), 10_000),
                    "The test document provider receives a successful exact-stage export. " +
                            safDiagnostics(monitor));
            Intent captured = monitor.capturedIntent();
            require(captured != null && "image/png".equals(captured.getType()),
                    "SAF save requests PNG for the PNG source. " + safDiagnostics(monitor));
            require(String.valueOf(captured.getStringExtra(Intent.EXTRA_TITLE)).contains(titleToken),
                    "SAF title identifies the visible " + stageName + " stage. " + safDiagnostics(monitor));
            try (InputStream stream = getContext().getContentResolver()
                    .openInputStream(TatappTestDocumentProvider.SUCCESS_URI)) {
                Bitmap actual = BitmapFactory.decodeStream(stream);
                require(actual != null, "The exported SAF document decodes as an image.");
                try {
                    require(bitmapsEqual(expected, actual),
                            "The exported pixels equal the visible Fine outline preview. " +
                                    safDiagnostics(monitor));
                } finally {
                    actual.recycle();
                }
            }
        } finally {
            removeMonitor(monitor);
        }
    }

    private void testSafSaveFailure() throws Exception {
        RecordingActivityMonitor monitor = new RecordingActivityMonitor(Intent.ACTION_CREATE_DOCUMENT,
                new ActivityResult(Activity.RESULT_OK,
                        new Intent().setData(TatappTestDocumentProvider.FAILURE_URI)));
        addMonitor(monitor);
        try {
            click("SAVE current look");
            require(waitUntil(() -> findContaining(root(), "The image could not be saved") != null &&
                            isButtonEnabled("SAVE current look"), 8_000),
                    "A provider write failure is reported without a false success. " +
                            safDiagnostics(monitor));
            require(exactTextExists(stageLabel(8, "Fine outline")),
                    "Save failure preserves the visible semantic stage. " + safDiagnostics(monitor));
            requireButton(root(), "SAVE current look", true);
            sendKeyDownUpSync(KeyEvent.KEYCODE_BACK);
            waitForIdleSync();
        } finally {
            removeMonitor(monitor);
        }
    }

    private void testRapidImageReplacement() throws Exception {
        Uri older = createPngFixture("rapid-older.png", 1200, 900, Color.RED);
        Uri current = createPngFixture("rapid-current.png", 120, 80, Color.BLUE);
        dispatchActivityResultWithoutIdle(1001, Activity.RESULT_OK,
                readableInputResult(older));
        dispatchActivityResultWithoutIdle(1001, Activity.RESULT_OK,
                readableInputResult(current));
        require(waitUntil(() -> findContaining(root(), "rapid-current.png") != null &&
                exactTextExists(stageLabel(1, "Original image")), 12_000),
                "The newest rapidly selected source becomes the active Original image.");
        Thread.sleep(500);
        require(findContaining(root(), "rapid-older.png") == null,
                "The superseded import cannot publish after the current source.");
    }

    private void testCorruptInputRecovery() throws Exception {
        Uri corrupt = createInputDocument("corrupt-input.png");
        try (OutputStream stream = requireFixtureOutput("corrupt-input.png")) {
            stream.write(new byte[] { 0x54, 0x41, 0x54, 0x41, 0x50, 0x50 });
        }
        require(waitUntil(() -> documentSize(corrupt) == 6, 3_000),
                "Corrupt fixture did not settle in the test provider; size=" + documentSize(corrupt) + ".");
        dispatchActivityResult(1001, Activity.RESULT_OK, readableInputResult(corrupt));
        require(waitUntil(() -> findContaining(root(), "The photo could not be opened") != null &&
                        isButtonEnabled("SAVE current look"), 8_000),
                "A corrupt image produces a named recoverable error.");
        require(findContaining(root(), "rapid-current.png") != null,
                "Corrupt replacement preserves the prior workspace source.");
        requireButton(root(), "SAVE current look", true);
        sendKeyDownUpSync(KeyEvent.KEYCODE_BACK);
        waitForIdleSync();
    }

    private void testTouchTargets() {
        waitForIdleSync();
        float density = activity.getResources().getDisplayMetrics().density;
        int minimum = (int) Math.floor(48 * density) - 1;
        int count = 0;
        List<String> undersized = new ArrayList<>();
        for (View view : descendants(root())) {
            if (!isVisibilityChainVisible(view) || !view.isEnabled() || !isActionControl(view)) continue;
            count++;
            if (view.getWidth() < minimum || view.getHeight() < minimum) undersized.add(describe(view));
        }
        require(count > 0, "At least one visible actionable control was measured.");
        require(undersized.isEmpty(), "Undersized controls: " + String.join(", ", undersized));
    }

    private void testReducedMotion() {
        Switch control = null;
        for (View view : descendants(root())) {
            if (view instanceof Switch && "Reduce anatomy motion".contentEquals(((Switch) view).getText())) {
                control = (Switch) view;
                break;
            }
        }
        require(control != null, "Reduced-motion Switch is present.");
        Switch target = control;
        runOnMainSync(target::performClick);
        waitForIdleSync();
        require(target.isChecked(), "Reduced motion can be enabled by activation.");
        require(target.getContentDescription().toString().toLowerCase(Locale.ROOT).contains("without animation"),
                "Reduced-motion semantics explain the non-animated effect.");
    }

    private void testOfflineAiFence() throws Exception {
        CheckBox control = null;
        for (View view : descendants(root())) {
            if (view instanceof CheckBox && "Offline AI Describe".contentEquals(((CheckBox) view).getText())) {
                control = (CheckBox) view;
                break;
            }
        }
        require(control != null, "Offline AI Describe checkbox is present.");
        CheckBox target = control;
        runOnMainSync(target::performClick);
        waitUntil(() -> !target.isChecked(), 1_500);
        if (target.isChecked()) {
            // A suitable tested tier must ask for consent before any network or
            // installation work. Back cancels that native dialog without opting in.
            sendKeyDownUpSync(KeyEvent.KEYCODE_BACK);
        }
        require(waitUntil(() -> !target.isChecked(), 5_000),
                "Unavailable or declined model leaves the checkbox off.");
        require(findContaining(root(), "descriptions are ready") == null, "No false ready claim is present.");
    }

    private void testPickerCancellation() throws Exception {
        dispatchActivityResult(1001, Activity.RESULT_CANCELED, null);
        require(waitUntil(() -> findContaining(root(), "Photo selection canceled") != null, 3_000),
                "Picker cancellation is reported.");
        require(findContaining(root(), "rapid-current.png") != null,
                "Picker cancellation preserves the source.");
    }

    private void testCameraCancellation() throws Exception {
        dispatchActivityResult(1002, Activity.RESULT_CANCELED, null);
        require(waitUntil(() -> findContaining(root(), "Camera capture canceled") != null, 3_000),
                "Camera cancellation is reported.");
        require(findContaining(root(), "rapid-current.png") != null,
                "Camera cancellation preserves the source.");
    }

    private void testBlackWidowActivation() throws Exception {
        RecordingActivityMonitor monitor = new RecordingActivityMonitor(Intent.ACTION_VIEW,
                new ActivityResult(Activity.RESULT_CANCELED, null));
        addMonitor(monitor);
        try {
            TextView view = findExact(root(), "BLACK WIDOW TATTOO");
            require(view instanceof Button, "BLACK WIDOW TATTOO is a native Button.");
            Button button = (Button) view;
            runOnMainSync(button::requestFocus);
            waitForIdleSync();
            require(monitor.capturedIntent() == null,
                    "Focusing BLACK WIDOW TATTOO does not navigate externally.");
            runOnMainSync(() -> require(button.performClick(),
                    "BLACK WIDOW TATTOO rejected deliberate activation."));
            require(waitUntil(() -> monitor.capturedIntent() != null, 3_000),
                    "Deliberate activation emits an external view intent.");
            Intent captured = monitor.capturedIntent();
            require(Intent.ACTION_VIEW.equals(captured.getAction()) &&
                            "https://www.facebook.com/grayscaleconsultants".equals(String.valueOf(captured.getData())) &&
                            captured.hasCategory(Intent.CATEGORY_BROWSABLE),
                    "External activation is browsable and uses the exact repository URL.");
        } finally {
            removeMonitor(monitor);
        }
    }

    private void testLowMemoryRecovery() throws Exception {
        String source = activeSourceName();
        invokeActivityIntMethod("onTrimMemory", LEGACY_TRIM_MEMORY_RUNNING_CRITICAL);
        invokeActivityMethod("onLowMemory");
        waitForIdleSync();
        require(findContaining(root(), source) != null,
                "Critical-memory callbacks preserve reconstructable source state.");
        requireButton(root(), "SAVE current look", true);
        click("Next Stage");
        require(waitUntil(() -> findContaining(root(), "Ready. Stage 2 of") != null, 8_000),
                "Stage rendering remains usable after critical-memory cleanup.");
    }

    private void testBackAndResume() throws Exception {
        invokeActivityMethod("onBackPressed");
        waitForIdleSync();
        TextView resume = findExact(root(), "Resume workspace");
        require(resume instanceof Button && isVisibilityChainVisible(resume),
                "Back exposes Resume workspace on Start.");
        runOnMainSync(resume::performClick);
        waitForIdleSync();
        require(findContaining(root(), "Workspace resumed") != null, "Resume returns to the current workspace.");
    }

    private void testRecreation() throws Exception {
        Spinner region = findSpinner("Body region");
        int rightCalf = findSpinnerItemContaining(region, "Right calf");
        SeekBar stage = findSeekBar("Visual development stage");
        SeekBar bodySize = findSeekBar("Body size");
        SeekBar skinTone = findSeekBar("Skin tone and complexion");
        Switch reducedMotion = requireSwitch("Reduce anatomy motion");
        RadioButton female = requireRadioButton("Female");
        require(region != null && rightCalf >= 0 && stage != null && bodySize != null && skinTone != null,
                "Non-default restoration controls are present.");
        runOnMainSync(() -> {
            female.performClick();
            region.setSelection(rightCalf);
            setSeekBarProgress(bodySize, 40);
            setSeekBarProgress(skinTone, 78);
            if (!reducedMotion.isChecked()) reducedMotion.performClick();
            setSeekBarProgress(stage, 21);
        });
        require(waitUntil(() -> exactTextExists(stageLabel(4, "Binarized stencil")) &&
                exactTextExists("Ready. " + stageLabel(4, "Binarized stencil") + "."), 8_000),
                "A non-default stage is settled before recreation. " + stageDiagnostics());

        Activity previous = activity;
        tracker.prepare(previous);
        runOnMainSync(previous::recreate);
        Activity replacement = tracker.waitForReplacement(8_000);
        require(replacement != null, "A replacement Activity resumed after recreate().");
        activity = replacement;
        waitForIdleSync();
        require(waitUntil(() -> findContaining(root(), "rapid-current.png") != null, 10_000),
                "Source identity survives recreation.");
        require(findContaining(root(), stageLabel(4, "Binarized stencil")) != null,
                "Non-default stage identity survives recreation. " + stageDiagnostics());
        Spinner restoredRegion = findSpinner("Body region");
        View restoredAnatomy = findByContentPrefix("Interactive female anatomical preview");
        require(restoredRegion != null && restoredRegion.getSelectedItemPosition() ==
                        findSpinnerItemContaining(restoredRegion, "Right calf") && restoredAnatomy != null,
                "Sex and region survive recreation.");
        String description = restoredAnatomy.getContentDescription().toString().toLowerCase(Locale.ROOT);
        require(description.contains("female") && description.contains("right calf") &&
                        description.contains("180 centimeters") && description.contains("medium brown"),
                "Model size, complexion, sex, and region semantics survive recreation.");
        require(requireSwitch("Reduce anatomy motion").isChecked(),
                "Reduced-motion preference survives recreation.");
        requireButton(root(), "SAVE current look", true);
    }

    private void testLargeTextCompactLayout() {
        View windowRoot = root();
        List<TextView> textViews = new ArrayList<>();
        List<Float> originalSizes = new ArrayList<>();
        for (View view : descendants(windowRoot)) {
            if (view instanceof TextView) {
                textViews.add((TextView) view);
                originalSizes.add(((TextView) view).getTextSize());
            }
        }
        int originalWidth = windowRoot.getWidth();
        int originalHeight = windowRoot.getHeight();
        float density = activity.getResources().getDisplayMetrics().density;
        int compactWidth = Math.min(originalWidth, Math.round(320 * density));
        try {
            runOnMainSync(() -> {
                for (int index = 0; index < textViews.size(); index++)
                    textViews.get(index).setTextSize(TypedValue.COMPLEX_UNIT_PX, originalSizes.get(index) * 2f);
                windowRoot.measure(View.MeasureSpec.makeMeasureSpec(compactWidth, View.MeasureSpec.EXACTLY),
                        View.MeasureSpec.makeMeasureSpec(originalHeight, View.MeasureSpec.EXACTLY));
                windowRoot.layout(0, 0, compactWidth, originalHeight);
            });
            waitForIdleSync();
            require(findFirst(root(), ScrollView.class) != null,
                    "Large text and compact width retain the scrolling content container.");
            testTouchTargets();
            requireButton(root(), "SAVE current look", true);
            require(findSpinner("Body region") != null && findSeekBar("Visual development stage") != null,
                    "Core workspace controls remain laid out under 2x text and 320dp width.");
        } finally {
            runOnMainSync(() -> {
                for (int index = 0; index < textViews.size(); index++)
                    textViews.get(index).setTextSize(TypedValue.COMPLEX_UNIT_PX, originalSizes.get(index));
                windowRoot.measure(View.MeasureSpec.makeMeasureSpec(originalWidth, View.MeasureSpec.EXACTLY),
                        View.MeasureSpec.makeMeasureSpec(originalHeight, View.MeasureSpec.EXACTLY));
                windowRoot.layout(0, 0, originalWidth, originalHeight);
            });
            waitForIdleSync();
        }
    }

    private void testOrientationChange() throws Exception {
        Activity previous = activity;
        int current = previous.getResources().getConfiguration().orientation;
        int desired = current == android.content.res.Configuration.ORIENTATION_LANDSCAPE
                ? ActivityInfo.SCREEN_ORIENTATION_PORTRAIT : ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE;
        tracker.prepare(previous);
        runOnMainSync(() -> previous.setRequestedOrientation(desired));
        Activity replacement = tracker.waitForReplacement(8_000);
        if (replacement == null) {
            skip("orientation recreation result", "Connected target ignored the requested orientation change.");
            return;
        }
        activity = replacement;
        waitForIdleSync();
        require(waitUntil(() -> findContaining(root(), "rapid-current.png") != null, 10_000),
                "Workspace source restores after accepted orientation change.");
        require(findContaining(root(), stageLabel(4, "Binarized stencil")) != null,
                "Stage survives accepted orientation recreation.");
        require(requireRadioButton("Female").isChecked() &&
                        findSpinner("Body region").getSelectedItem().toString().contains("Right calf"),
                "Non-default anatomy survives accepted orientation recreation.");
        requireButton(root(), "SAVE current look", true);
    }

    private Uri createPngFixture() throws Exception {
        return createPngFixture("instrumentation-source.png", 96, 64, Color.WHITE);
    }

    private Uri createPngFixture(String name, int width, int height, int background) throws Exception {
        Uri input = createInputDocument(name);
        Bitmap bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
        Canvas canvas = new Canvas(bitmap);
        canvas.drawColor(background);
        Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        paint.setColor(Color.BLACK);
        paint.setStrokeWidth(Math.max(7, width / 32f));
        canvas.drawLine(width * 0.1f, height * 0.82f, width * 0.5f, height * 0.16f, paint);
        canvas.drawLine(width * 0.5f, height * 0.16f, width * 0.9f, height * 0.82f, paint);
        canvas.drawCircle(width * 0.5f, height * 0.58f, Math.min(width, height) * 0.2f, paint);
        try (OutputStream stream = requireFixtureOutput(name)) {
            require(bitmap.compress(Bitmap.CompressFormat.PNG, 100, stream), "Fixture PNG encoding failed.");
        } finally {
            bitmap.recycle();
        }
        require(waitUntil(() -> documentSize(input) > 0, 3_000),
                "PNG fixture did not settle in the test provider: " + name +
                        ", size=" + documentSize(input) + ".");
        return input;
    }

    private Uri createInputDocument(String name) {
        Uri input = TatappTestDocumentProvider.inputUri(name);
        getContext().getContentResolver().delete(input, null, null);
        temporaryInputs.add(input);
        return input;
    }

    private OutputStream requireFixtureOutput(String name) throws Exception {
        OutputStream output = getContext().getContentResolver().openOutputStream(
                TatappTestDocumentProvider.setupInputUri(name), "wt");
        if (output == null) throw new IllegalStateException("Fixture provider did not open an output stream.");
        return output;
    }

    private long documentSize(Uri uri) {
        try (Cursor cursor = getContext().getContentResolver().query(uri,
                new String[] { OpenableColumns.SIZE }, null, null, null)) {
            if (cursor == null || !cursor.moveToFirst()) return 0;
            int column = cursor.getColumnIndex(OpenableColumns.SIZE);
            return column < 0 || cursor.isNull(column) ? 0 : cursor.getLong(column);
        }
    }

    private static Intent readableInputResult(Uri uri) {
        return new Intent()
                .setData(uri)
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
    }

    private static void setSeekBarProgress(SeekBar seekBar, int value) {
        if (seekBar.getProgress() == value) return;
        Bundle arguments = new Bundle();
        arguments.putFloat(AccessibilityNodeInfo.ACTION_ARGUMENT_PROGRESS_VALUE, value);
        seekBar.performAccessibilityAction(
                AccessibilityNodeInfo.AccessibilityAction.ACTION_SET_PROGRESS.getId(), arguments);
    }

    private String anatomyDiagnostics(Spinner region, SeekBar bodySize, SeekBar skinTone, View anatomy) {
        Object selected = region == null ? null : region.getSelectedItem();
        return "Actual anatomy state: region=" + selected +
                ", regionIndex=" + (region == null ? -1 : region.getSelectedItemPosition()) +
                ", bodyProgress=" + (bodySize == null ? -1 : bodySize.getProgress()) +
                ", toneProgress=" + (skinTone == null ? -1 : skinTone.getProgress()) +
                ", description=" + (anatomy == null ? "<missing>" : anatomy.getContentDescription()) + ".";
    }

    private String stageDiagnostics() {
        SeekBar stage = findSeekBar("Visual development stage");
        TextView status = findByContentPrefixAsText("Application status.");
        return "Actual stage state: progress=" + (stage == null ? -1 : stage.getProgress()) +
                ", sliderDescription=" + (stage == null ? "<missing>" : stage.getContentDescription()) +
                ", statusText=" + (status == null ? "<missing>" : status.getText()) +
                ", statusDescription=" + (status == null ? "<missing>" : status.getContentDescription()) + ".";
    }

    private String safDiagnostics(RecordingActivityMonitor monitor) {
        Intent captured = monitor.capturedIntent();
        return "Actual SAF state: monitorHits=" + monitor.getHits() +
                ", capturedIntent=" + (captured == null ? "<none>" : captured) +
                ", providerSize=" + documentSize(TatappTestDocumentProvider.SUCCESS_URI) + ". " +
                stageDiagnostics();
    }

    private void dispatchActivityResult(int requestCode, int resultCode, Intent data) throws Exception {
        dispatchActivityResultWithoutIdle(requestCode, resultCode, data);
        waitForIdleSync();
    }

    private void dispatchActivityResultWithoutIdle(int requestCode, int resultCode, Intent data) {
        final Throwable[] failure = new Throwable[1];
        runOnMainSync(() -> {
            try {
                activity.getClass().getMethod("onActivityResult", int.class, int.class, Intent.class)
                        .invoke(activity, requestCode, resultCode, data);
            } catch (Throwable error) {
                failure[0] = error;
            }
        });
        if (failure[0] != null) throw new IllegalStateException("Could not dispatch Activity result.", failure[0]);
    }

    private void invokeActivityMethod(String methodName) throws Exception {
        final Throwable[] failure = new Throwable[1];
        runOnMainSync(() -> {
            try {
                activity.getClass().getMethod(methodName).invoke(activity);
            } catch (Throwable error) {
                failure[0] = error;
            }
        });
        if (failure[0] != null) throw new IllegalStateException("Could not invoke " + methodName + ".", failure[0]);
    }

    private void invokeActivityIntMethod(String methodName, int value) {
        final Throwable[] failure = new Throwable[1];
        runOnMainSync(() -> {
            try {
                activity.getClass().getMethod(methodName, int.class).invoke(activity, value);
            } catch (Throwable error) {
                failure[0] = error;
            }
        });
        if (failure[0] != null)
            throw new IllegalStateException("Could not invoke " + methodName + ".", failure[0]);
    }

    private void click(String text) {
        TextView view = findExact(root(), text);
        require(view instanceof Button, "Button not found: " + text);
        Button button = (Button) view;
        runOnMainSync(() -> require(button.performClick(), "Button rejected activation: " + text));
        waitForIdleSync();
    }

    private View root() {
        require(activity != null && activity.getWindow() != null, "Current Activity window is unavailable.");
        return activity.getWindow().getDecorView();
    }

    private Spinner findSpinner(String contentDescription) {
        for (View view : descendants(root())) {
            if (view instanceof Spinner && contentDescription.contentEquals(view.getContentDescription()))
                return (Spinner) view;
        }
        return null;
    }

    private SeekBar findSeekBar(String contentPrefix) {
        for (View view : descendants(root())) {
            CharSequence description = view.getContentDescription();
            if (view instanceof SeekBar && description != null &&
                    description.toString().startsWith(contentPrefix)) return (SeekBar) view;
        }
        return null;
    }

    private RadioButton requireRadioButton(String text) {
        TextView view = findExact(root(), text);
        require(view instanceof RadioButton, text + " is a native RadioButton.");
        return (RadioButton) view;
    }

    private Switch requireSwitch(String text) {
        TextView view = findExact(root(), text);
        require(view instanceof Switch, text + " is a native Switch.");
        return (Switch) view;
    }

    private View findByContentPrefix(String prefix) {
        for (View view : descendants(root())) {
            CharSequence description = view.getContentDescription();
            if (description != null && description.toString().startsWith(prefix)) return view;
        }
        return null;
    }

    private TextView findByContentPrefixAsText(String prefix) {
        View view = findByContentPrefix(prefix);
        return view instanceof TextView ? (TextView) view : null;
    }

    private ImageView findImagePreview() {
        for (View view : descendants(root())) {
            CharSequence description = view.getContentDescription();
            if (view instanceof ImageView && description != null &&
                    description.toString().startsWith("Preview of ")) return (ImageView) view;
        }
        return null;
    }

    private String activeSourceName() {
        for (View view : descendants(root())) {
            CharSequence description = view.getContentDescription();
            if (view instanceof TextView && description != null &&
                    description.toString().startsWith("Selected photo. ")) {
                String value = ((TextView) view).getText().toString();
                int separator = value.indexOf(" — ");
                return separator < 0 ? value : value.substring(0, separator);
            }
        }
        throw new AssertionError("Active source metadata was not found.");
    }

    private boolean isButtonEnabled(String text) {
        TextView view = findExact(root(), text);
        return view instanceof Button && view.isEnabled();
    }

    private int findSpinnerItemContaining(Spinner spinner, String text) {
        if (spinner == null) return -1;
        String needle = text.toLowerCase(Locale.ROOT);
        for (int index = 0; index < spinner.getCount(); index++) {
            if (String.valueOf(spinner.getItemAtPosition(index)).toLowerCase(Locale.ROOT).contains(needle))
                return index;
        }
        return -1;
    }

    private boolean isTouchExplorationEnabled() {
        android.view.accessibility.AccessibilityManager manager =
                (android.view.accessibility.AccessibilityManager)
                        activity.getSystemService(android.content.Context.ACCESSIBILITY_SERVICE);
        return manager != null && manager.isTouchExplorationEnabled();
    }

    private boolean selectDifferentRegionByModelTouch(View anatomy, Spinner region, int original) throws Exception {
        runOnMainSync(() -> {
            anatomy.requestFocus();
            anatomy.requestRectangleOnScreen(new Rect(0, 0, anatomy.getWidth(), anatomy.getHeight()), true);
            anatomy.invalidate();
        });
        waitForIdleSync();
        for (int row = 1; row < 10; row++) {
            for (int column = 1; column < 10; column++) {
                float x = anatomy.getWidth() * column / 10f;
                float y = anatomy.getHeight() * row / 10f;
                long time = SystemClock.uptimeMillis();
                runOnMainSync(() -> {
                    MotionEvent down = MotionEvent.obtain(time, time, MotionEvent.ACTION_DOWN, x, y, 0);
                    MotionEvent up = MotionEvent.obtain(time, time + 16, MotionEvent.ACTION_UP, x, y, 0);
                    try {
                        anatomy.dispatchTouchEvent(down);
                        anatomy.dispatchTouchEvent(up);
                    } finally {
                        down.recycle();
                        up.recycle();
                    }
                });
                waitForIdleSync();
                if (region.getSelectedItemPosition() != original) return true;
            }
        }
        return false;
    }

    private static void assertSelectedRegionMatchesAnatomy(Spinner region, View anatomy) {
        String selection = String.valueOf(region.getSelectedItem()).toLowerCase(Locale.ROOT);
        String description = String.valueOf(anatomy.getContentDescription()).toLowerCase(Locale.ROOT);
        String[] semanticWords = {
                "left", "right", "inner", "outer", "upper", "arm", "forearm", "wrist",
                "chest", "abdomen", "back", "shoulder", "thigh", "calf"
        };
        for (String word : semanticWords) {
            if (selection.contains(word)) require(description.contains(word),
                    "Rendered-model semantics do not match picker word: " + word);
        }
    }

    private static boolean bitmapsEqual(Bitmap expected, Bitmap actual) {
        if (expected.getWidth() != actual.getWidth() || expected.getHeight() != actual.getHeight()) return false;
        int[] expectedPixels = new int[expected.getWidth()];
        int[] actualPixels = new int[actual.getWidth()];
        for (int y = 0; y < expected.getHeight(); y++) {
            expected.getPixels(expectedPixels, 0, expected.getWidth(), 0, y, expected.getWidth(), 1);
            actual.getPixels(actualPixels, 0, actual.getWidth(), 0, y, actual.getWidth(), 1);
            for (int x = 0; x < expectedPixels.length; x++)
                if (expectedPixels[x] != actualPixels[x]) return false;
        }
        return true;
    }

    private Bitmap captureView(View view) {
        require(view.getWidth() > 0 && view.getHeight() > 0,
                "The view must be laid out before pixel capture.");
        Bitmap[] captured = new Bitmap[1];
        runOnMainSync(() -> {
            Bitmap bitmap = Bitmap.createBitmap(view.getWidth(), view.getHeight(), Bitmap.Config.ARGB_8888);
            Canvas canvas = new Canvas(bitmap);
            try {
                view.draw(canvas);
            } finally {
                canvas.setBitmap(null);
            }
            captured[0] = bitmap;
        });
        require(captured[0] != null, "View pixel capture failed.");
        return captured[0];
    }

    private static List<View> descendants(View root) {
        List<View> result = new ArrayList<>();
        appendDescendants(root, result);
        return result;
    }

    private static void appendDescendants(View view, List<View> result) {
        result.add(view);
        if (!(view instanceof ViewGroup)) return;
        ViewGroup group = (ViewGroup) view;
        for (int index = 0; index < group.getChildCount(); index++) appendDescendants(group.getChildAt(index), result);
    }

    private static TextView findExact(View root, String text) {
        for (View view : descendants(root)) {
            if (view instanceof TextView && text.contentEquals(((TextView) view).getText())) return (TextView) view;
        }
        return null;
    }

    private static TextView findContaining(View root, String text) {
        String needle = text.toLowerCase(Locale.ROOT);
        for (View view : descendants(root)) {
            if (view instanceof TextView && ((TextView) view).getText().toString().toLowerCase(Locale.ROOT).contains(needle))
                return (TextView) view;
        }
        return null;
    }

    private boolean exactTextExists(String text) {
        return findExact(root(), text) != null;
    }

    private static int countExactText(View root, String text) {
        int count = 0;
        for (View view : descendants(root)) {
            if (view instanceof TextView && text.contentEquals(((TextView) view).getText())) count++;
        }
        return count;
    }

    private static <T extends View> T findFirst(View root, Class<T> type) {
        for (View view : descendants(root)) {
            if (type.isInstance(view)) return type.cast(view);
        }
        return null;
    }

    private static int indexOfText(List<View> views, String text) {
        for (int index = 0; index < views.size(); index++) {
            View view = views.get(index);
            if (view instanceof TextView && text.contentEquals(((TextView) view).getText())) return index;
        }
        return -1;
    }

    private String stagePrefix(int index) {
        require(stageCount > 0, "Stage total was not discovered from the application UI.");
        return "Stage " + index + " of " + stageCount + ":";
    }

    private String readyStagePrefix(int index) {
        return "Ready. " + stagePrefix(index);
    }

    private String stageLabel(int index, String name) {
        return stagePrefix(index) + " " + name;
    }

    private static boolean isActionControl(View view) {
        return view instanceof Button || view instanceof CheckBox || view instanceof RadioButton ||
                view instanceof Switch || view instanceof Spinner || view instanceof SeekBar;
    }

    private static boolean isVisibilityChainVisible(View view) {
        View current = view;
        while (current != null) {
            if (current.getVisibility() != View.VISIBLE) return false;
            if (!(current.getParent() instanceof View)) break;
            current = (View) current.getParent();
        }
        return true;
    }

    private static String describe(View view) {
        String label = view instanceof TextView ? " '" + ((TextView) view).getText() + "'" : "";
        return view.getClass().getSimpleName() + label + " (" + view.getWidth() + "x" + view.getHeight() + ")";
    }

    private static void requireButton(View root, String text, boolean enabled) {
        TextView view = findExact(root, text);
        require(view instanceof Button, text + " is a native Button.");
        Button button = (Button) view;
        require(button.isEnabled() == enabled, text + " enabled state should be " + enabled + ".");
        require(button.getContentDescription() != null && button.getContentDescription().length() > 0,
                text + " has no accessible name/hint.");
    }

    private static void require(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }

    private static boolean waitUntil(Condition condition, long timeoutMs) throws Exception {
        long deadline = System.currentTimeMillis() + timeoutMs;
        while (System.currentTimeMillis() < deadline) {
            if (condition.test()) return true;
            Thread.sleep(50);
        }
        return condition.test();
    }

    private void test(String name, CheckedRunnable action) {
        try {
            action.run();
            passed++;
            report.add("PASS: " + name);
        } catch (Throwable error) {
            fail(name, error.getMessage() == null ? error.getClass().getName() : error.getMessage());
        }
    }

    private void fail(String name, String detail) {
        failed++;
        report.add("FAIL: " + name + " — " + detail);
    }

    private void skip(String name, String reason) {
        skipped++;
        report.add("SKIP: " + name + " — " + reason);
    }

    private void cleanup() {
        try {
            if (tracker != null) ((Application) getTargetContext().getApplicationContext())
                    .unregisterActivityLifecycleCallbacks(tracker);
            for (Uri temporaryInput : temporaryInputs)
                getContext().getContentResolver().delete(temporaryInput, null, null);
            getContext().getContentResolver().delete(TatappTestDocumentProvider.SUCCESS_URI, null, null);
        } catch (Throwable error) {
            report.add("CLEANUP WARNING: " + error.getMessage());
        }
    }

    private static String stack(Throwable error) {
        StringBuilder text = new StringBuilder(error.toString());
        for (StackTraceElement element : error.getStackTrace()) text.append("\n  at ").append(element);
        return text.toString();
    }

    private interface CheckedRunnable { void run() throws Exception; }
    private interface Condition { boolean test() throws Exception; }

    private static final class RecordingActivityMonitor extends ActivityMonitor {
        private final String expectedAction;
        private final ActivityResult result;
        private volatile Intent captured;

        RecordingActivityMonitor(String expectedAction, ActivityResult result) {
            this.expectedAction = expectedAction;
            this.result = result;
        }

        @Override public ActivityResult onStartActivity(Intent intent) {
            if (!expectedAction.equals(intent.getAction())) return null;
            captured = new Intent(intent);
            return result;
        }

        Intent capturedIntent() { return captured; }
    }

    private static final class ActivityTracker implements Application.ActivityLifecycleCallbacks {
        private final String packageName;
        private volatile Activity previous;
        private volatile Activity replacement;
        private volatile CountDownLatch resumed = new CountDownLatch(1);

        ActivityTracker(String packageName) { this.packageName = packageName; }
        void prepare(Activity oldActivity) {
            previous = oldActivity;
            replacement = null;
            resumed = new CountDownLatch(1);
        }
        Activity waitForReplacement(long timeoutMs) throws InterruptedException {
            return resumed.await(timeoutMs, TimeUnit.MILLISECONDS) ? replacement : null;
        }
        @Override public void onActivityResumed(Activity candidate) {
            if (!packageName.equals(candidate.getPackageName()) || candidate == previous) return;
            replacement = candidate;
            resumed.countDown();
        }
        @Override public void onActivityCreated(Activity activity, Bundle state) { }
        @Override public void onActivityStarted(Activity activity) { }
        @Override public void onActivityPaused(Activity activity) { }
        @Override public void onActivityStopped(Activity activity) { }
        @Override public void onActivitySaveInstanceState(Activity activity, Bundle state) { }
        @Override public void onActivityDestroyed(Activity activity) { }
    }
}
