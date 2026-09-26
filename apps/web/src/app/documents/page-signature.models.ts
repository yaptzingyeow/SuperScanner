export interface SignatureBox { x: number; y: number; width: number; height: number }
export interface PageSignatureDto {
  id: string;
  pageId: string;
  box: SignatureBox;
  imageAspectRatio: number;
  revision: number;
  imageUrl: string;
}
export interface SignatureView extends PageSignatureDto { localImageUrl: string }
export interface SignatureDraft { id: string; box: SignatureBox; imageAspectRatio: number; localImageUrl: string }
