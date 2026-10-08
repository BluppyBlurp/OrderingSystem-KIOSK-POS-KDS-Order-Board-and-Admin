import { ApiError, createApiClient, unwrap, type Problem, type Schemas } from "@kiosk/api-client";
import { useQuery } from "@tanstack/react-query";
import { config } from "./config";

export type Category = Schemas["CategoryDto"];
export type Product = Schemas["AdminProductDto"];
export type ProductInput = Schemas["UpsertProductRequest"];
export type ModifierGroup = Schemas["ModifierGroupDto"];
export type Modifier = Schemas["ModifierDto"];
export type Media = Schemas["MediaDto"];
export type MediaType = Schemas["MediaDto"]["type"];
export type Device = Schemas["DeviceDto"];
export type DeviceKind = Schemas["DeviceDto"]["kind"];
export type SalesReport = Schemas["SalesReportDto"];
export type RefundNeeded = Schemas["RefundNeededDto"];

/** Set once the manager is signed in (Clerk session token, or the dev token in development). */
let tokenSource: () => Promise<string | null> = async () => null;
export function setTokenSource(source: () => Promise<string | null>) {
  tokenSource = source;
}

export const api = createApiClient(config.apiUrl, () => tokenSource());

/** For endpoints that answer 204 No Content (delete, reorder, revoke), where `unwrap` has no body to return. */
async function done(call: Promise<{ error?: unknown; response: Response }>): Promise<void> {
  const { error, response } = await call;
  if (!response.ok) throw new ApiError(response.status, (error ?? {}) as Problem);
}

export const errorText = (e: unknown) => (e instanceof Error ? e.message : "Something went wrong.");

// ---------- Query keys and hooks ----------

export const keys = {
  categories: ["admin", "categories"],
  products: ["admin", "products"],
  groups: ["admin", "modifier-groups"],
  devices: ["admin", "devices"],
  sales: ["admin", "sales"],
  refunds: ["admin", "refunds"],
};

export const useCategories = () =>
  useQuery({ queryKey: keys.categories, queryFn: () => unwrap(api.GET("/api/admin/categories")) });

/** All products; the menu page filters by category on the client (one list, so moves between categories just work). */
export const useProducts = () =>
  useQuery({ queryKey: keys.products, queryFn: () => unwrap(api.GET("/api/admin/products", { params: { query: {} } })) });

export const useModifierGroups = () =>
  useQuery({ queryKey: keys.groups, queryFn: () => unwrap(api.GET("/api/admin/modifier-groups")) });

export const useDevices = () => useQuery({ queryKey: keys.devices, queryFn: () => unwrap(api.GET("/api/admin/devices")) });

export const useSales = (from: string, to: string) =>
  useQuery({
    queryKey: [...keys.sales, from, to],
    queryFn: () => unwrap(api.GET("/api/admin/reports/sales", { params: { query: { from, to } } })),
    enabled: !!from && !!to,
  });

export const useRefundsNeeded = () =>
  useQuery({ queryKey: keys.refunds, queryFn: () => unwrap(api.GET("/api/admin/reports/refunds-needed")) });

// ---------- Categories ----------

export const createCategory = (body: Schemas["UpsertCategoryRequest"]) => unwrap(api.POST("/api/admin/categories", { body }));
export const updateCategory = (id: string, body: Schemas["UpsertCategoryRequest"]) =>
  unwrap(api.PUT("/api/admin/categories/{id}", { params: { path: { id } }, body }));
export const deleteCategory = (id: string) => done(api.DELETE("/api/admin/categories/{id}", { params: { path: { id } } }));
export const reorderCategories = (ids: string[]) => done(api.PUT("/api/admin/categories/order", { body: { ids } }));

// ---------- Products ----------

export const createProduct = (body: ProductInput) => unwrap(api.POST("/api/admin/products", { body }));
export const updateProduct = (id: string, body: ProductInput) =>
  unwrap(api.PUT("/api/admin/products/{id}", { params: { path: { id } }, body }));
export const deleteProduct = (id: string) => done(api.DELETE("/api/admin/products/{id}", { params: { path: { id } } }));
export const reorderProducts = (ids: string[]) => done(api.PUT("/api/admin/products/order", { body: { ids } }));
export const setStock = (id: string, stock: number | null) =>
  unwrap(api.PATCH("/api/admin/products/{id}/stock", { params: { path: { id } }, body: { stock } }));
export const setAvailability = (id: string, isAvailable: boolean) =>
  unwrap(api.PATCH("/api/admin/products/{id}/availability", { params: { path: { id } }, body: { isAvailable } }));

// ---------- Media ----------

export const addMediaByUrl = (productId: string, body: Schemas["AddMediaRequest"]) =>
  unwrap(api.POST("/api/admin/products/{id}/media", { params: { path: { id: productId } }, body }));
export const deleteMedia = (productId: string, mediaId: string) =>
  done(api.DELETE("/api/admin/products/{id}/media/{mediaId}", { params: { path: { id: productId, mediaId } } }));
export const presignMedia = (body: Schemas["PresignMediaRequest"]) => unwrap(api.POST("/api/admin/media/presign", { body }));
export const completeMediaUpload = (productId: string, body: Schemas["CompleteMediaUploadRequest"]) =>
  unwrap(api.POST("/api/admin/products/{id}/media/uploaded", { params: { path: { id: productId } }, body }));

// ---------- Modifier groups ----------

export const createGroup = (body: Schemas["UpsertModifierGroupRequest"]) => unwrap(api.POST("/api/admin/modifier-groups", { body }));
export const updateGroup = (id: string, body: Schemas["UpsertModifierGroupRequest"]) =>
  unwrap(api.PUT("/api/admin/modifier-groups/{id}", { params: { path: { id } }, body }));
export const deleteGroup = (id: string) => done(api.DELETE("/api/admin/modifier-groups/{id}", { params: { path: { id } } }));
export const createModifier = (groupId: string, body: Schemas["UpsertModifierRequest"]) =>
  unwrap(api.POST("/api/admin/modifier-groups/{groupId}/modifiers", { params: { path: { groupId } }, body }));
export const updateModifier = (groupId: string, id: string, body: Schemas["UpsertModifierRequest"]) =>
  unwrap(api.PUT("/api/admin/modifier-groups/{groupId}/modifiers/{id}", { params: { path: { groupId, id } }, body }));
export const deleteModifier = (groupId: string, id: string) =>
  done(api.DELETE("/api/admin/modifier-groups/{groupId}/modifiers/{id}", { params: { path: { groupId, id } } }));

// ---------- Devices ----------

export const registerDevice = (body: Schemas["RegisterDeviceRequest"]) => unwrap(api.POST("/api/admin/devices", { body }));
export const revokeDevice = (id: string) => done(api.POST("/api/admin/devices/{id}/revoke", { params: { path: { id } } }));

// ---------- Staff ----------

export type StaffOverview = Schemas["StaffOverviewDto"];
export type StaffMember = Schemas["StaffMemberDto"];
export type StaffInvitation = Schemas["StaffInvitation"];

export const staffKey = ["admin", "staff"];

export const useStaff = () => useQuery({ queryKey: staffKey, queryFn: () => unwrap(api.GET("/api/admin/staff")) });

/** After accepting, the new staff member lands back on this app. */
export const inviteStaff = (email: string, role: string) =>
  unwrap(api.POST("/api/admin/staff/invitations", { body: { email, role, redirectUrl: window.location.origin } }));
export const revokeInvitation = (id: string) =>
  done(api.POST("/api/admin/staff/invitations/{id}/revoke", { params: { path: { id } } }));
export const setStaffRole = (userId: string, role: string) =>
  unwrap(api.PUT("/api/admin/staff/{userId}/role", { params: { path: { userId } }, body: { role } }));
export const removeStaffAccess = (userId: string) =>
  done(api.POST("/api/admin/staff/{userId}/remove-access", { params: { path: { userId } } }));
export const rejectSignup = (userId: string) => done(api.DELETE("/api/admin/staff/{userId}", { params: { path: { userId } } }));
