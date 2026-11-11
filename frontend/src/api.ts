export const API = import.meta.env.VITE_API_URL || "http://localhost:8080";
export type SchoolClass = {id:string;name:string;totalSeats:number;heldSeats:number;confirmedSeats:number;availableSeats:number};
export type Hold = {holdId:string;classId:string;studentId?:string;className?:string;status:string;expiresAt:string};
async function request<T>(path:string, init:RequestInit={}, studentId?:string):Promise<T>{
  const headers = new Headers(init.headers);
  headers.set("Content-Type","application/json");
  if(studentId) headers.set("X-Student-Id",studentId);
  const res = await fetch(`${API}${path}`,{...init,headers});
  if(res.status===204) return undefined as T;
  const body = await res.json().catch(()=>({error:"Unexpected server response"}));
  if(!res.ok) throw new Error(body.error || `Request failed (${res.status})`);
  return body as T;
}
export const api = {
  classes:()=>request<SchoolClass[]>("/classes"),
  createClass:(name:string,totalSeats:number)=>request("/classes",{method:"POST",body:JSON.stringify({name,totalSeats})}),
  createHold:(classId:string,studentId:string)=>request<Hold>(`/classes/${classId}/holds`,{method:"POST"},studentId),
  confirm:(holdId:string,studentId:string)=>request<{holdId:string;status:string}>(`/holds/${holdId}/confirm`,{method:"POST"},studentId),
  cancel:(holdId:string,studentId:string)=>request<void>(`/holds/${holdId}`,{method:"DELETE"},studentId),
  myHolds:(studentId:string)=>request<Hold[]>("/me/holds",{},studentId),
  getHold:(holdId:string)=>request<Hold>(`/holds/${holdId}`)
};
