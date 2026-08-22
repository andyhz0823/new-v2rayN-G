package com.v2ray.ang.xboard

import android.net.Uri
import org.json.JSONArray
import org.json.JSONObject

/**
 * Parse the authenticated Xboard getSubscribe payload into independent v2rayNG
 * subscription records. Invoke this after the app performs the Xboard login
 * and GET /api/v1/user/getSubscribe with the returned Bearer auth_data token.
 */
data class XboardSubscriptionProfile(
    val subscribeUrl: String,
    val name: String? = null,
    val planId: Long? = null,
    val subscriptionId: Long? = null,
    val upload: Long? = null,
    val download: Long? = null,
    val total: Long? = null,
    val expireAt: Long? = null,
    val source: String? = null,
    val isExternal: Boolean = false
) {
    fun isUsable(nowSeconds: Long = System.currentTimeMillis() / 1000): Boolean {
        val expiry = expireAt ?: 0L
        val seconds = if (expiry > 9_999_999_999L) expiry / 1000 else expiry
        if (seconds > 0 && seconds <= nowSeconds) return false
        return total == null || (upload ?: 0L) + (download ?: 0L) < total
    }
}

object XboardSubscriptionParser {
    fun parse(envelope: JSONObject, panelUrl: String): List<XboardSubscriptionProfile> {
        if (!envelope.optString("status").equals("success", ignoreCase = true)) {
            throw IllegalArgumentException(envelope.optString("message", "Xboard request failed"))
        }
        val data = envelope.optJSONObject("data")
            ?: throw IllegalArgumentException("Xboard response data is invalid")
        return parseData(data, Uri.parse(normalizeHttpUrl(panelUrl).trimEnd('/') + "/"))
    }

    fun parseData(data: JSONObject, panelUri: Uri): List<XboardSubscriptionProfile> {
        val items = firstArray(data, "subscriptions", "plans", "profiles")
        val result = linkedMapOf<String, XboardSubscriptionProfile>()
        for (index in 0 until items.length()) {
            val item = items.optJSONObject(index) ?: continue
            val rawUrl = firstString(item, "subscribe_url", "subscription_url", "subscribeUrl", "url", "link")
            if (rawUrl.isBlank()) continue
            val resolved = Uri.parse(normalizeHttpUrl(rawUrl))
            val external = if (item.has("is_external")) item.optBoolean("is_external")
            else !resolved.host.equals(panelUri.host, ignoreCase = true)
            val profile = XboardSubscriptionProfile(
                subscribeUrl = normalizeSubscriptionUrl(rawUrl, panelUri, external),
                name = firstString(item, "plan_name", "name", "title").ifBlank { null },
                planId = longOrNull(item, "plan_id"),
                subscriptionId = longOrNull(item, "id", "subscription_id"),
                upload = longOrNull(item, "u", "upload"),
                download = longOrNull(item, "d", "download"),
                total = longOrNull(item, "transfer_enable", "total"),
                expireAt = longOrNull(item, "expired_at", "expire_at", "expire"),
                source = firstString(item, "source").ifBlank { null },
                isExternal = external
            )
            result.putIfAbsent(profile.subscriptionId?.let { "id:$it" } ?: "url:${profile.subscribeUrl}", profile)
        }
        if (result.isEmpty()) {
            val fallback = firstString(data, "subscribe_url", "subscription_url", "subscribeUrl", "url", "link")
            if (fallback.isNotBlank()) result["url:$fallback"] = XboardSubscriptionProfile(
                subscribeUrl = normalizeSubscriptionUrl(fallback, panelUri, null),
                name = firstString(data, "plan_name", "name", "title").ifBlank { null },
                planId = longOrNull(data, "plan_id")
            )
        }
        return result.values.toList()
    }

    fun normalizeHttpUrl(value: String): String = value.trim().let {
        if (it.contains("://")) it else "https://$it"
    }

    /** Add the client flag only for a same-origin Xboard URL; external URLs remain byte-for-byte untouched. */
    fun normalizeSubscriptionUrl(value: String, panelUri: Uri, external: Boolean?): String {
        val normalized = normalizeHttpUrl(value)
        val uri = Uri.parse(normalized)
        if (external == true || !uri.host.equals(panelUri.host, ignoreCase = true)) return normalized
        if (uri.getQueryParameter("flag") != null) return normalized
        return uri.buildUpon().appendQueryParameter("flag", "v2rayn-g").build().toString()
    }

    private fun firstArray(obj: JSONObject, vararg keys: String): JSONArray {
        keys.forEach { key -> obj.optJSONArray(key)?.let { return it } }
        return JSONArray()
    }

    private fun firstString(obj: JSONObject, vararg keys: String): String {
        keys.forEach { key ->
            if (obj.has(key) && !obj.isNull(key)) obj.optString(key).trim().takeIf { it.isNotBlank() }?.let { return it }
        }
        return ""
    }

    private fun longOrNull(obj: JSONObject, vararg keys: String): Long? =
        firstString(obj, *keys).toLongOrNull()
}
