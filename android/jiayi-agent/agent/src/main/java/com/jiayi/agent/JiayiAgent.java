package com.jiayi.agent;

import android.content.Context;

import com.chaquo.python.PyObject;
import com.chaquo.python.Python;
import com.chaquo.python.android.AndroidPlatform;

public final class JiayiAgent {
    private JiayiAgent() {
    }

    public static String reply(
            Context context,
            String corpusPath,
            String soulPath,
            String stateDir,
            String text,
            String baseUrl,
            String model,
            String apiKey) {
        try {
            if (!Python.isStarted()) {
                Python.start(new AndroidPlatform(context));
            }
            PyObject module = Python.getInstance().getModule("persona_dialogue.phone_agent");
            PyObject result = module.callAttr(
                    "reply", corpusPath, soulPath, stateDir, text, baseUrl, model, apiKey);
            return result == null ? "{\"ok\":false,\"text\":\"agent 没有返回。\"}" : result.toString();
        } catch (Throwable error) {
            String message = error.getMessage() == null ? error.getClass().getSimpleName() : error.getMessage();
            return "{\"ok\":false,\"text\":" + org.json.JSONObject.quote(message) + "}";
        }
    }

    public static String remember(Context context, String stateDir, String text) {
        try {
            if (!Python.isStarted()) {
                Python.start(new AndroidPlatform(context));
            }
            PyObject module = Python.getInstance().getModule("persona_dialogue.phone_agent");
            PyObject result = module.callAttr("remember", stateDir, text);
            return result == null ? "{\"ok\":false,\"text\":\"agent 没有返回。\"}" : result.toString();
        } catch (Throwable error) {
            String message = error.getMessage() == null ? error.getClass().getSimpleName() : error.getMessage();
            return "{\"ok\":false,\"text\":" + org.json.JSONObject.quote(message) + "}";
        }
    }
}
