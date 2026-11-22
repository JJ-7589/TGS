const API_BASE_URL = "http://localhost:5150/api";

export function getToken() {
  return localStorage.getItem("caninos_token");
}

export function getCurrentUser() {
  const raw = localStorage.getItem("caninos_user");
  try {
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

export function setSession(authData) {
  localStorage.setItem("caninos_token", authData.token);
  localStorage.setItem("caninos_user", JSON.stringify(authData.usuario));
}

export function clearSession() {
  localStorage.removeItem("caninos_token");
  localStorage.removeItem("caninos_user");
}

/**
 * Función genérica para consumir los endpoints del backend ASP.NET Core.
 */
export async function apiRequest(path, { method = "GET", body, auth = true } = {}) {
  const headers = {};

  if (body !== undefined) {
    headers["Content-Type"] = "application/json";
  }

  if (auth) {
    const token = getToken();
    if (token) {
      headers["Authorization"] = `Bearer ${token}`;
    }
  }

  let response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      method,
      headers,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  } catch {
    const networkErr = new Error(
      "No se pudo conectar con el servidor (http://localhost:5150). Verifica que el backend esté ejecutándose."
    );
    networkErr.status = 0;
    networkErr.errors = {};
    throw networkErr;
  }

  // 204 No Content (usado en DELETE) no devuelve cuerpo JSON
  if (response.status === 204) {
    return null;
  }

  const data = await response.json().catch(() => null);

  if (!response.ok) {
    if (response.status === 401 && auth) {
      clearSession();
      window.dispatchEvent(new Event("session-expired"));
    }

    const error = new Error(data?.message || `Error HTTP ${response.status}`);
    error.status = response.status;
    error.errors = data?.errors || {};
    throw error;
  }

  return data;
}